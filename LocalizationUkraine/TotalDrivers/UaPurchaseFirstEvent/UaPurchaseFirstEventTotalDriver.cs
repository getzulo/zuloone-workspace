#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;
// ZuloOne.Managers и ZuloOne.Totals целиком не открываются: имена
// TransactionCollection / TransactionPairCollection / ITotalsManager есть и в
// пространстве проводок документа, и в пространстве итогов.
using ITotalsManager = ZuloOne.Managers.ITotalsManager;
using TransactionCollection = ZuloOne.Totals.TransactionCollection;
using TransactionPairCollection = ZuloOne.Totals.TransactionPairCollection;

// ═══ ДРАЙВЕР ПЕРШОЇ ПОДІЇ НА ВХОДЕ ═══════════════════════════════════════════
//
// Зеркало UaVatFirstEventTotalDriver, и по тем же причинам. Право на налоговый
// кредит рождает НЕ документ, а сдвиг базы по поставщику (ПКУ 198.2): кредит
// возникает на дату того из событий, что раньше — списание денег или получение
// товара. Правило одно, а документов, его рождающих, три: приход, оплата
// поставщику, возврат. Копия правила в каждом транзакционном скрипте — способ
// их разъехать.
//
// И СДЕЛАТЬ ЭТО В ПРОВОДКЕ НЕЛЬЗЯ, даже захотев: прирост зависит от СОСТОЯНИЯ
// (сколько уже зачтено), состояние читается асинхронно, а GetTransactions
// синхронна.
//
// ═══ РЕЖИМ РЕШАЕТ, ПОЛОЖЕН ЛИ КРЕДИТ ВООБЩЕ ══════════════════════════════════
//
// Налоговый кредит — право ПЛАТЕЛЬЩИКА ПДВ. Спрощенець на 5% и неплательщик на
// общей системе входящий ПДВ не зачитывают: для них он часть стоимости товара.
// Поэтому у драйвера ровно одна развилка, и она по тому же полю юрлица, что и на
// продажах: ChargesVat — кредит есть, иначе движений нет вовсе.
//
// ЕДИНОГО НАЛОГА ЗДЕСЬ НЕТ и быть не может: ЄП считается с ДОХОДА, а закупка
// дохода не создаёт. Расход базу ЄП не уменьшает — это не та система.
public partial class UaPurchaseFirstEventTotalDriver
{
    // Ключ — ИЗМЕРЕНИЯ: TransactionBase знает только координаты, аналитик у него
    // нет вовсе. Объяви ключ аналитикой — и IsCoordinateNull отбросит каждую
    // проводку молча: движения записаны, кредит не зачтён, в логе пусто.
    private const string Received = "Received";
    private const string PaidOut = "PaidOut";
    private const string Supplier = "Supplier";
    private const string Entity = "LegalEntity";

    // Поставщики, которых коснулся документ. Значения не копим — остатки всё
    // равно читаются из регистра, важен только сам факт, что поставщик задет.
    private readonly HashSet<(Guid Entity, Guid Supplier)> _touched = new();

    public override void ValidateTransactions(
        TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        base.ValidateTransactions(transactionPairs, transactions);

        foreach (var tv in transactions)
        {
            if (tv.TotalDescriptor.Guid != TotalID) continue;
            if (tv.IsCoordinateNull(Supplier)) continue;
            if (tv.IsValueNull(Received) && tv.IsValueNull(PaidOut)) continue;

            // Юрлицо на пустоту НЕ проверяем: пустое — законная координата, и
            // ведёт она себя как «режим не указан», то есть как плательщик ПДВ.
            var entity = tv.IsCoordinateNull(Entity) ? Guid.Empty : tv.GetCoordinate(Entity);
            _touched.Add((entity, tv.GetCoordinate(Supplier)));
        }
    }

    public override void EndDocument(DateTime transactionDate, Guid docId)
    {
        base.EndDocument(transactionDate, docId);

        var suppliers = _touched.ToList();
        // Экземпляр драйвера живёт одно проведение, но обнуляем явно: EndDocument
        // — публичный хук, и повторный вызов не должен зачесть кредит дважды.
        _touched.Clear();
        if (suppliers.Count == 0) return;

        CreditAsync(suppliers, transactionDate, docId).GetAwaiter().GetResult();
    }

    private async Task CreditAsync(
        List<(Guid Entity, Guid Supplier)> suppliers, DateTime movementDate, Guid docId)
    {
        var rate = await RateOnAsync(movementDate);
        if (rate <= 0m) return; // контур не настроен — молчим, как и на продажах

        var firstEvent = GetService<IUaFirstEvent>();
        var tax = GetService<ITaxService>();
        var movements = GetService<IRegisterMovementService>();

        var registers = await GetService<IMetadataService>().GetAllRegistersAsync();
        var eventId = registers.First(r =>
            string.Equals(r.Name, "UaPurchaseFirstEvent", StringComparison.OrdinalIgnoreCase)).MetaId;
        var creditId = registers.First(r =>
            string.Equals(r.Name, "UaVatCredit", StringComparison.OrdinalIgnoreCase)).MetaId;

        foreach (var key in suppliers)
        {
            var regime = await firstEvent.RegimeOfAsync(key.Entity);
            // Неплательщик ПДВ входящий налог не зачитывает — для него это часть
            // стоимости товара. Движений не пишем вообще: Credited у такого
            // юрлица должен остаться нулевым, чтобы смена режима на плательщика
            // не обнаружила «уже зачтённую» базу, которой никогда не было.
            if (!firstEvent.ChargesVat(regime)) continue;

            var creditable = await firstEvent.CreditIncrementAsync(key.Entity, key.Supplier, rate);
            // Знак не трогаем: плюс — зачёт, минус — снятие по возврату. Ноль
            // означает «событие не первое» и не пишется.
            if (creditable == 0m) continue;

            var vat = tax.CalculateTax(Math.Abs(creditable), rate);
            if (vat == 0m) continue;
            if (creditable < 0m) vat = -vat;

            var coordinates = new Dictionary<string, object?>
            {
                [Entity] = key.Entity,
                [Supplier] = key.Supplier,
            };

            await movements.PostMovementAsync(
                eventId, docId, movementDate, coordinates,
                new Dictionary<string, decimal> { ["Credited"] = creditable });

            await movements.PostMovementAsync(
                creditId, docId, movementDate, coordinates,
                new Dictionary<string, decimal> { ["Amount"] = vat });
        }
    }

    /// <summary>
    /// Ставка на дату движения по настроенному коду по умолчанию. Документа здесь
    /// нет — у драйвера только координаты регистра, — поэтому ставка берётся из
    /// контура. Даты совпадают: движение пишется датой документа, значит задним
    /// числом посчитается историческая ставка.
    /// </summary>
    private async Task<decimal> RateOnAsync(DateTime date)
    {
        var tax = GetService<ITaxService>();
        var code = await tax.ResolveDefaultTaxCodeAsync();
        if (code is not Guid codeId) return 0m;
        return await tax.ResolveRateAsync(codeId, date) ?? 0m;
    }
}
