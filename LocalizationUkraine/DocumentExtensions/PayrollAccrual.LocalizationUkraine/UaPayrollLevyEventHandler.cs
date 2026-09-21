#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Events;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Удержания из зарплаты: ПДФО и военный сбор.
//
// ПОЧЕМУ ОБРАБОТЧИК, А НЕ ТРАНЗАКЦИОННЫЙ СКРИПТ. GetTransactions синхронный, а
// здесь нужны ТРИ асинхронных чтения на каждое начисление: настройки, код
// налога, ставка на дату. Обычный выход — застолбить результат в поле шапки
// before-update-событием — тут не работает: удержания ПЕРСОНАЛЬНЫЕ, их столько
// же, сколько строк, и в шапку они не помещаются.
//
// ЦЕНА ЭТОГО ВЫБОРА ЧЕСТНАЯ: движения, записанные руками, платформа сама не
// сторнирует при аннулировании — в отличие от проводок транзакционного скрипта.
// Поэтому снятие удержаний написано явно, в OnAfterPost при переходе в Voided,
// и это единственное место, где оно живёт.
//
// ЗВЕНО 20: после владельца (он создаёт соцвзнос) и после GL (он проводит
// начисление в учёт). Нам нужно, чтобы к моменту удержания обязательство перед
// работником уже стояло в регистре целиком — иначе уменьшать будет нечего.
[ExtensionOf("PayrollAccrual")]
public partial class UaPayrollLevyEventHandler : TypedDocumentEventHandler<PayrollAccrual>
{
    public override async Task<EventResult> OnAfterPostAsync(
        PayrollAccrual document, EventContext context)
    {
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype == PayrollAccrual.Subtypes.Voided)
        {
            await ReleaseAsync(document, context);
            return EventResult.Ok();
        }

        if (document.Subtype != PayrollAccrual.Subtypes.Posted) return EventResult.Ok();

        await WithholdAsync(document, context);
        return EventResult.Ok();
    }

    private async Task WithholdAsync(PayrollAccrual document, EventContext context)
    {
        var levies = context.GetService<IUaPayrollLevies>();
        var entity = await levies.LegalEntityOfDivisionAsync(document.Division);
        if (entity == Guid.Empty) return;

        var movements = context.GetService<IRegisterMovementService>();
        var registers = await RegisterIdsAsync(context);

        // ИДЕМПОТЕНТНОСТЬ. Проведение может отработать по одному документу не
        // один раз: движение, записанное отсюда, поднимает пересчёт итогов, а
        // тот снова проходит цепочку событий. Ключ — сам документ: если по нему
        // в регистре удержаний уже что-то есть, второй проход не нужен.
        var already = await movements.QueryMovementsAsync(
            registers.Levy, $"DocumentMetaId = '{document.MetaId}'", take: 1);
        if (already.Count > 0) return;

        var date = document.DocumentDate == default ? DateTime.UtcNow : document.DocumentDate;

        // Начисления по одному человеку складываем ДО расчёта. Ставки здесь
        // плоские, так что на сумму это не влияет, — но влияет на округление:
        // две строки по 1000,005 дают разный налог, смотря считать их порознь
        // или вместе. Складываем по той же причине, по которой это делает HR.
        var gross = new Dictionary<Guid, decimal>();
        foreach (var line in document.Lines)
        {
            if (line.Employee == Guid.Empty) continue;
            gross[line.Employee] = (gross.TryGetValue(line.Employee, out var v) ? v : 0m) + line.Amount;
        }

        var direction = await DirectionAsync(context);

        foreach (var pair in gross)
        {
            var withheld = 0m;

            foreach (var levy in await levies.WithholdingsAsync(pair.Value, date))
            {
                // Персонифицированно — для 4ДФ, где каждая строка это человек.
                await movements.PostMovementAsync(
                    registers.Levy, document.MetaId, date,
                    new Dictionary<string, object?>
                    {
                        ["LegalEntity"] = entity,
                        ["Employee"] = pair.Key,
                        ["TaxCode"] = levy.TaxCodeId,
                    },
                    new Dictionary<string, decimal>
                    {
                        ["Base"] = levy.Base,
                        ["Amount"] = levy.Amount,
                    });

                // И обезличенно — в общий налоговый леджер, откуда декларацию
                // собирает та же машинка, что и по ПДВ с единым налогом.
                await movements.PostMovementAsync(
                    registers.Ledger, document.MetaId, date,
                    new Dictionary<string, object?>(),
                    new Dictionary<string, decimal>
                    {
                        ["TaxBase"] = levy.Base,
                        ["TaxAmount"] = levy.Amount,
                    },
                    analytics: new Dictionary<string, object?>
                    {
                        ["TaxCode"] = levy.TaxCodeId,
                        ["TaxDirection"] = direction,
                        ["LegalEntity"] = entity,
                    });

                withheld += levy.Amount;
            }

            if (withheld <= 0m) continue;

            // Вот ради этой строки всё и затевалось: на руки человек получает
            // начисленное МИНУС удержанное. Пока обязательство не уменьшено,
            // выплата по нему уйдёт в полной сумме.
            //
            // Withheld — та же сумма вторым ресурсом, и это не дубль. Amount
            // одинаково уменьшают удержание и выплата, а HR при аннулировании
            // обязан их различить: после выплаты аннулировать нельзя, после
            // удержания — можно. Не заполнив Withheld, мы сделали бы каждое
            // украинское начисление неаннулируемым.
            await movements.PostMovementAsync(
                registers.Liability, document.MetaId, date,
                new Dictionary<string, object?>(),
                new Dictionary<string, decimal> { ["Amount"] = -withheld, ["Withheld"] = withheld },
                analytics: new Dictionary<string, object?> { ["Employee"] = pair.Key });
        }
    }

    /// <summary>
    /// Аннулирование. Платформа снимает движения транзакционных скриптов сама,
    /// но эти записаны руками — значит и снимать их руками.
    ///
    /// И ВОТ ГДЕ ЛЕГКО ИСПОРТИТЬ ЧУЖОЕ. «Удалить движения документа» — операция
    /// по РЕГИСТРУ ЦЕЛИКОМ, а не по автору: в PayrollLiability тем же
    /// документом пишет транзакционный скрипт HR, и снос по документу унёс бы
    /// начисленное обязательство вместе с нашим удержанием. Поэтому удаляем
    /// только там, где мы единственный писатель (UaPayrollLevy, а в леджер по
    /// документу ФОТ кроме нас никто не ходит), а обязательство ВОЗВРАЩАЕМ
    /// встречным движением.
    ///
    /// Сколько возвращать — читаем из СВОЕГО регистра, а не пересчитываем:
    /// ставка с тех пор могла смениться, и пересчёт вернул бы не ту сумму,
    /// которую удержали.
    /// </summary>
    private async Task ReleaseAsync(PayrollAccrual document, EventContext context)
    {
        var movements = context.GetService<IRegisterMovementService>();
        var registers = await RegisterIdsAsync(context);

        var mine = await movements.QueryMovementsAsync(
            registers.Levy, $"DocumentMetaId = '{document.MetaId}'");
        if (mine.Count == 0) return;

        var withheld = new Dictionary<Guid, decimal>();
        foreach (var row in mine)
        {
            if (row.TryGetValue("Employee", out var raw) && raw is not null
                && Guid.TryParse(Convert.ToString(raw), out var employee))
            {
                var amount = row.TryGetValue("Amount", out var a) && a is not null
                    ? Convert.ToDecimal(a) : 0m;
                withheld[employee] = (withheld.TryGetValue(employee, out var v) ? v : 0m) + amount;
            }
        }

        await movements.DeleteDocumentMovementsAsync(registers.Levy, document.MetaId);
        await movements.DeleteDocumentMovementsAsync(registers.Ledger, document.MetaId);

        var date = document.DocumentDate == default ? DateTime.UtcNow : document.DocumentDate;
        foreach (var pair in withheld)
        {
            if (pair.Value <= 0m) continue;
            await movements.PostMovementAsync(
                registers.Liability, document.MetaId, date,
                new Dictionary<string, object?>(),
                new Dictionary<string, decimal> { ["Amount"] = pair.Value, ["Withheld"] = -pair.Value },
                analytics: new Dictionary<string, object?> { ["Employee"] = pair.Key });
        }
    }

    private static async Task<(Guid Levy, Guid Ledger, Guid Liability)> RegisterIdsAsync(
        EventContext context)
    {
        var all = await context.GetService<IMetadataService>().GetAllRegistersAsync();
        Guid Id(string name) => all.First(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)).MetaId;
        return (Id("UaPayrollLevy"), Id("TaxLedger"), Id("PayrollLiability"));
    }

    private static async Task<Guid> DirectionAsync(EventContext context)
    {
        var rows = await context.GetService<IDictionaryManager>()
            .GetRecordsAsync<TaxDirection>("Code = 'OUTPUT'", take: 1);
        return rows.Count > 0 ? rows[0].MetaId : Guid.Empty;
    }
}
