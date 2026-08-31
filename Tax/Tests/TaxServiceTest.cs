using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Покрытие TaxService: синхронный расчёт налога (база × ставка, округлённый) и
// подбор ДЕЙСТВУЮЩЕЙ ставки по налоговому коду и дате.
//
// Почему проверок так много. Ставка ищется по окну EffectiveFrom…EffectiveTo, и
// одиночная проверка «бессрочная ставка подошла» проходит ОДИНАКОВО и когда окно
// учитывается, и когда EffectiveTo просто игнорируется. Различают эти два мира
// только пары: истёкшая ставка НЕ подходит, будущая ещё НЕ подходит, а при смене
// ставки берётся та, что действовала в дату документа. Плюс отдельно — что окна
// налога и кода тоже читаются, что пересечение окон отвергается, и что
// отсутствие ставки не превращается молча в ноль.
public class TaxServiceTest : IntegrationTestScriptBase
{
    private static ITaxService Svc => GetService<ITaxService>();

    /// <summary>Код налога с собственным налогом и категорией; ставки заводит сам тест.</summary>
    private sealed class Fixture
    {
        public Guid TaxId;
        public Guid CodeId;
    }

    // Коды справочников уникальны: фиксированные значения ломали бы тест на
    // стенде, где такая запись уже заведена. Db.NewId() — законный остаток:
    // генерация идентификатора.
    private string Uniq() => $"{Db.NewId():N}"[..8];

    /// <summary>
    /// Налог + категория. Ссылки на тип/орган/юрисдикцию заполняются сгенерированными
    /// id: справочники в них не участвуют, а обязательность поля соблюдена.
    /// </summary>
    private async Task<Guid> NewTaxAsync(string uniq, DateTime from, DateTime? to = null)
        => await NewRecordAsync<Tax>(t =>
        {
            t.Code = $"T-{uniq}";
            t.Name = "Test tax";
            t.TaxType = Db.NewId();
            t.Authority = Db.NewId();
            t.Jurisdiction = Db.NewId();
            t.EffectiveFrom = from;
            t.EffectiveTo = to;
        });

    private async Task<Guid> NewRateAsync(Guid taxId, string code, decimal rate, DateTime from, DateTime? to = null)
        => await NewRecordAsync<TaxRate>(r =>
        {
            r.Tax = taxId;
            r.Code = code;
            r.Rate = rate;
            r.EffectiveFrom = from;
            r.EffectiveTo = to;
        });

    /// <summary>Код налога. <paramref name="anchorRate"/> — обязательная ссылка TaxCode.TaxRate.</summary>
    private async Task<Guid> NewCodeAsync(Guid taxId, string uniq, Guid anchorRate, DateTime from, DateTime? to = null)
    {
        var category = await NewRecordAsync<TaxCategory>(c =>
        {
            c.Tax = taxId;
            c.Code = $"STD-{uniq}";
            c.Treatment = "STANDARD";
        });
        return await NewRecordAsync<TaxCode>(c =>
        {
            c.Code = $"VAT-{uniq}";
            c.Name = "Test code";
            c.Tax = taxId;
            c.TaxCategory = category;
            c.TaxRate = anchorRate;
            c.EffectiveFrom = from;
            c.EffectiveTo = to;
        });
    }

    /// <summary>Налог, одна ставка и код на неё — самая частая расстановка.</summary>
    private async Task<Fixture> OneRateAsync(
        decimal rate, DateTime rateFrom, DateTime? rateTo = null,
        DateTime? taxTo = null, DateTime? codeTo = null)
    {
        var uniq = Uniq();
        var from = new DateTime(2020, 1, 1);
        var taxId = await NewTaxAsync(uniq, from, taxTo);
        var rateId = await NewRateAsync(taxId, $"R-{uniq}", rate, rateFrom, rateTo);
        var codeId = await NewCodeAsync(taxId, uniq, rateId, from, codeTo);
        return new Fixture { TaxId = taxId, CodeId = codeId };
    }

    private static async Task<decimal> RateOnAsync(Guid code, DateTime on)
    {
        var r = await Svc.ResolveRateAsync(code, on);
        Assert.IsNotNull(r, "на {0:yyyy-MM-dd} ставка должна разрешаться", on);
        return r!.Value;
    }

    [IntegrationTest("Сумма налога = база × ставка, округлённая до денежной точности")]
    public async Task CalculatesTax()
    {
        await Task.CompletedTask;

        Assert.IsTrue(Svc.CalculateTax(100m, 0.15m) == 15m, "100 × 0.15 = 15, факт {0}", Svc.CalculateTax(100m, 0.15m));
        Assert.IsTrue(Svc.CalculateTax(33.33m, 0.2m) == 6.67m, "33.33 × 0.2 = 6.666 → 6.67, факт {0}", Svc.CalculateTax(33.33m, 0.2m));
        Assert.IsTrue(Svc.CalculateTax(0m, 0.15m) == 0m, "0 × 0.15 = 0, факт {0}", Svc.CalculateTax(0m, 0.15m));
    }

    [IntegrationTest("Бессрочная ставка (EffectiveTo = NULL) действует и сегодня, и через десять лет")]
    public async Task OpenEndedRateApplies()
    {
        var f = await OneRateAsync(0.15m, new DateTime(2020, 1, 1)); // EffectiveTo не задан

        var today = await Svc.ResolveRateAsync(f.CodeId);
        Assert.IsTrue(today == 0.15m, "ставка кода на сегодня = 0.15, факт {0}", today.HasValue ? today.Value : -1m);

        var far = await RateOnAsync(f.CodeId, DateTime.UtcNow.Date.AddYears(10));
        Assert.IsTrue(far == 0.15m, "бессрочная ставка действует и через 10 лет, факт {0}", far);

        var amount = await Svc.CalculateByCodeAsync(200m, f.CodeId);
        Assert.IsTrue(amount == 30m, "200 × 0.15 = 30, факт {0}", amount);
    }

    [IntegrationTest("Истёкшая ставка не подбирается — после EffectiveTo её нет")]
    public async Task ExpiredRateIsNotSelected()
    {
        var f = await OneRateAsync(0.15m, new DateTime(2020, 1, 1), new DateTime(2020, 12, 31));

        // Внутри окна ставка есть — значит null ниже говорит именно об окне,
        // а не о развалившейся расстановке данных.
        var inside = await RateOnAsync(f.CodeId, new DateTime(2020, 6, 1));
        Assert.IsTrue(inside == 0.15m, "внутри окна ставка 0.15, факт {0}", inside);

        // EffectiveTo — «действует ПО», последний день ВКЛЮЧЁН.
        var lastDay = await RateOnAsync(f.CodeId, new DateTime(2020, 12, 31));
        Assert.IsTrue(lastDay == 0.15m, "последний день окна включён, факт {0}", lastDay);

        Assert.IsNull(await Svc.ResolveRateAsync(f.CodeId, new DateTime(2021, 1, 1)),
            "на следующий день после EffectiveTo ставки быть не должно");
        Assert.IsNull(await Svc.ResolveRateAsync(f.CodeId),
            "сегодня, спустя годы после EffectiveTo, ставки быть не должно");
    }

    [IntegrationTest("Будущая ставка не подбирается — до EffectiveFrom её ещё нет")]
    public async Task FutureRateIsNotSelected()
    {
        var start = DateTime.UtcNow.Date.AddDays(30);
        var f = await OneRateAsync(0.20m, start); // открыта справа, но начинается в будущем

        Assert.IsNull(await Svc.ResolveRateAsync(f.CodeId),
            "ставка, вступающая в силу через 30 дней, сегодня не действует");
        Assert.IsNull(await Svc.ResolveRateAsync(f.CodeId, start.AddDays(-1)),
            "накануне EffectiveFrom ставки ещё нет");

        // Первый день окна ВКЛЮЧЁН.
        var firstDay = await RateOnAsync(f.CodeId, start);
        Assert.IsTrue(firstDay == 0.20m, "в день вступления в силу ставка 0.20, факт {0}", firstDay);
    }

    [IntegrationTest("Смена ставки: берётся та, что действовала в дату документа")]
    public async Task RateChangeSelectsTheRateOfTheDocumentDate()
    {
        var uniq = Uniq();
        var taxId = await NewTaxAsync(uniq, new DateTime(2020, 1, 1));
        var old = await NewRateAsync(taxId, $"R15-{uniq}", 0.15m, new DateTime(2020, 1, 1), new DateTime(2024, 12, 31));
        await NewRateAsync(taxId, $"R20-{uniq}", 0.20m, new DateTime(2025, 1, 1));

        // Код привязан к СТАРОЙ ставке: TaxCode.TaxRate фиксирует ставку на момент
        // заведения кода и устаревает при первом же её изменении. Если подбор
        // читает эту привязку вместо истории, документ 2025 года посчитается по
        // 15% — ровно та ошибка, которую тест обязан ловить.
        var code = await NewCodeAsync(taxId, uniq, old, new DateTime(2020, 1, 1));

        Assert.IsTrue(await RateOnAsync(code, new DateTime(2024, 6, 1)) == 0.15m, "в 2024 действует 0.15");
        Assert.IsTrue(await RateOnAsync(code, new DateTime(2024, 12, 31)) == 0.15m, "31.12.2024 — ещё старая ставка");
        Assert.IsTrue(await RateOnAsync(code, new DateTime(2025, 1, 1)) == 0.20m, "01.01.2025 — уже новая ставка");
        Assert.IsTrue(await RateOnAsync(code, new DateTime(2026, 6, 1)) == 0.20m, "в 2026 действует 0.20");

        var y2024 = await Svc.CalculateByCodeAsync(1000m, code, new DateTime(2024, 6, 1));
        Assert.IsTrue(y2024 == 150m, "1000 по ставке 2024 года = 150, факт {0}", y2024);
        var y2025 = await Svc.CalculateByCodeAsync(1000m, code, new DateTime(2025, 6, 1));
        Assert.IsTrue(y2025 == 200m, "1000 по ставке 2025 года = 200, факт {0}", y2025);
    }

    [IntegrationTest("Окна налога и налогового кода учитываются наравне с окном ставки")]
    public async Task TaxAndCodeWindowsAreHonoured()
    {
        var closed = new DateTime(2024, 12, 31);
        var inside = new DateTime(2024, 6, 1);

        // Ставка бессрочная, закрыт сам КОД.
        var byCode = await OneRateAsync(0.15m, new DateTime(2020, 1, 1), codeTo: closed);
        Assert.IsTrue(await RateOnAsync(byCode.CodeId, inside) == 0.15m, "пока код действует, ставка разрешается");
        Assert.IsNull(await Svc.ResolveRateAsync(byCode.CodeId, new DateTime(2025, 1, 1)),
            "вышедший из употребления код ставки не даёт, даже если сама ставка бессрочна");

        // Ставка бессрочная, отменён сам НАЛОГ.
        var byTax = await OneRateAsync(0.15m, new DateTime(2020, 1, 1), taxTo: closed);
        Assert.IsTrue(await RateOnAsync(byTax.CodeId, inside) == 0.15m, "пока налог действует, ставка разрешается");
        Assert.IsNull(await Svc.ResolveRateAsync(byTax.CodeId, new DateTime(2025, 1, 1)),
            "отменённый налог ставки не даёт, даже если сама ставка бессрочна");
    }

    [IntegrationTest("Отсутствие ставки на дату — отказ, а не молчаливый ноль")]
    public async Task MissingRateIsLoudNotZero()
    {
        var f = await OneRateAsync(0.15m, new DateTime(2020, 1, 1), new DateTime(2020, 12, 31));
        var code = await RecordAsync<TaxCode>(f.CodeId);
        Assert.IsNotNull(code, "код налога читается");

        // Ноль здесь неотличим от «не облагается»: счёт ушёл бы клиенту без НДС и
        // молча. Отказ обязан назвать код, иначе настройку не починить.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Svc.CalculateByCodeAsync(100m, f.CodeId),
            "расчёт по коду без действующей ставки обязан отказать");
        Assert.IsTrue(ex.Message.Contains(code!.Code),
            "в отказе должен быть назван налоговый код '{0}', факт: {1}", code.Code, ex.Message);
    }

    [IntegrationTest("Пересечение окон двух ставок одного налога отвергается")]
    public async Task AmbiguousRatesAreRejected()
    {
        var uniq = Uniq();
        var taxId = await NewTaxAsync(uniq, new DateTime(2020, 1, 1));
        // Старую ставку забыли закрыть — окна пересекаются с 2025 года.
        var open = await NewRateAsync(taxId, $"R15-{uniq}", 0.15m, new DateTime(2020, 1, 1));
        await NewRateAsync(taxId, $"R20-{uniq}", 0.20m, new DateTime(2025, 1, 1));
        var code = await NewCodeAsync(taxId, uniq, open, new DateTime(2020, 1, 1));

        // До пересечения ответ однозначен — значит отказ ниже вызван именно
        // пересечением, а не сломанной расстановкой.
        Assert.IsTrue(await RateOnAsync(code, new DateTime(2024, 6, 1)) == 0.15m,
            "до 2025 подходит ровно одна ставка");

        // Последним действием кейса: после отказа к базе не обращаемся.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Svc.ResolveRateAsync(code, new DateTime(2025, 6, 1)),
            "две ставки на одну дату — порча данных, а не повод выбрать любую");
        Assert.IsTrue(ex.Message.Contains($"R15-{uniq}") && ex.Message.Contains($"R20-{uniq}"),
            "отказ должен назвать обе пересекающиеся ставки, факт: {0}", ex.Message);
    }
}
