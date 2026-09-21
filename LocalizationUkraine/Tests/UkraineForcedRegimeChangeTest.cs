using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// ═══ ПРИНУДИТЕЛЬНЫЙ ПЕРЕХОД НА ОБЩУЮ СИСТЕМУ (ПКУ 293.8) ════════════════════
//
// Превысил годовой предел — с первого числа месяца, СЛЕДУЮЩЕГО ЗА КВАРТАЛОМ
// превышения, спрощенець переходит на общую систему. Не с даты превышения и не
// со следующего месяца: именно с квартальной границы, и на этом легче всего
// ошибиться.
//
// Решение принимается в момент превышения, а случается через недели, поэтому
// проверяются ДВЕ разные вещи: что переход ЗАПИСАН правильно и что он
// ПРИМЕНЁН в свой день, а не раньше.
public class UkraineForcedRegimeChangeTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static IUaFirstEvent FirstEvent => GetService<IUaFirstEvent>();

    private sealed class Setup
    {
        public Guid LegalEntity;
        public Guid Customer;
        public Guid Contract;
    }

    private async Task<Setup> SetupAsync(UaTaxRegime regime, decimal limit)
    {
        await Db.SetAccountingPeriodsAsync(null, null);

        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Hryvnia";
        currency.Code = $"U{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "₴";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Ukraine";
        country.CodeISO2 = $"{Db.NewId():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "380";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "FOP";
        legalEntity.RegistrationNumber = $"REG-FRC-{Db.NewId():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        await Db.UpdateAsync("LegalEntity", legalEntity.MetaId,
            new Dictionary<string, object?>
            {
                ["UaTaxRegime"] = (int)regime,
                ["Country"] = country.MetaId,
                ["Currency"] = currency.MetaId,
            });

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var outlet = DictionaryManager.NewRecord<CustomerOutlet>();
        outlet.Name = "Outlet";
        outlet.Customer = customer.MetaId;
        outlet = await DictionaryManager.SaveRecordAsync(outlet);

        var contract = DictionaryManager.NewRecord<SalesContract>();
        contract.Name = "UA-FRC";
        contract.Outlet = outlet.MetaId;
        contract.Currency = currency.MetaId;
        contract.SettlementKind = SettlementKind.Credit;
        contract.EffectiveFrom = new DateTime(2020, 1, 1);
        contract.LegalEntity = legalEntity.MetaId;
        contract = await DictionaryManager.SaveRecordAsync(contract);

        await TaxCircuitAsync();

        var row = DictionaryManager.NewRecord<UaSingleTaxLimit>();
        row.Code = $"LIM-{Db.NewId():N}"[..10];
        row.Amount = limit;
        row.EffectiveFrom = new DateTime(2020, 1, 1);
        await DictionaryManager.SaveRecordAsync(row);

        return new Setup
        {
            LegalEntity = legalEntity.MetaId,
            Customer = customer.MetaId,
            Contract = contract.MetaId,
        };
    }

    private async Task TaxCircuitAsync()
    {
        var from = new DateTime(2020, 1, 1);

        var authority = DictionaryManager.NewRecord<TaxAuthority>();
        authority.Code = $"AU-{Db.NewId():N}"[..10];
        authority.Name = "DPS";
        authority.CountryCode = "UA";
        authority.IsActive = true;
        authority = await DictionaryManager.SaveRecordAsync(authority);

        var jurisdiction = DictionaryManager.NewRecord<TaxJurisdiction>();
        jurisdiction.Code = $"JU-{Db.NewId():N}"[..10];
        jurisdiction.Name = "Ukraine";
        jurisdiction.CountryCode = "UA";
        jurisdiction.Level = 0;
        jurisdiction = await DictionaryManager.SaveRecordAsync(jurisdiction);

        var vat = DictionaryManager.NewRecord<Tax>();
        vat.Code = $"VT-{Db.NewId():N}"[..10];
        vat.Name = "Ukraine VAT";
        vat.Authority = authority.MetaId;
        vat.Jurisdiction = jurisdiction.MetaId;
        vat.EffectiveFrom = from;
        vat = await DictionaryManager.SaveRecordAsync(vat);

        var vatCategory = DictionaryManager.NewRecord<TaxCategory>();
        vatCategory.Tax = vat.MetaId;
        vatCategory.Code = $"STD-{Db.NewId():N}"[..10];
        vatCategory.Treatment = "STANDARD";
        vatCategory = await DictionaryManager.SaveRecordAsync(vatCategory);

        var vatRate = DictionaryManager.NewRecord<TaxRate>();
        vatRate.Tax = vat.MetaId;
        vatRate.TaxCategory = vatCategory.MetaId;
        vatRate.Code = $"R-{Db.NewId():N}"[..10];
        vatRate.Rate = 0.20m;
        vatRate.EffectiveFrom = from;
        vatRate = await DictionaryManager.SaveRecordAsync(vatRate);

        var vatCode = DictionaryManager.NewRecord<TaxCode>();
        vatCode.Code = $"OUT-{Db.NewId():N}"[..10];
        vatCode.Name = "Standard 20%";
        vatCode.Tax = vat.MetaId;
        vatCode.TaxCategory = vatCategory.MetaId;
        vatCode.TaxRate = vatRate.MetaId;
        vatCode.EffectiveFrom = from;
        vatCode = await DictionaryManager.SaveRecordAsync(vatCode);

        var ep = DictionaryManager.NewRecord<Tax>();
        ep.Code = $"EP-{Db.NewId():N}"[..10];
        ep.Name = "Single tax";
        ep.Authority = authority.MetaId;
        ep.Jurisdiction = jurisdiction.MetaId;
        ep.EffectiveFrom = from;
        ep = await DictionaryManager.SaveRecordAsync(ep);

        var three = await EpCodeAsync(ep, "EP3", 0.03m, from);
        var five = await EpCodeAsync(ep, "EP5", 0.05m, from);
        var over = await EpCodeAsync(ep, "E15", 0.15m, from);

        if ((await DictionaryManager.GetRecordsAsync<TaxDirection>("Code = 'OUTPUT'", take: 1)).Count == 0)
        {
            var direction = DictionaryManager.NewRecord<TaxDirection>();
            direction.Code = "OUTPUT";
            direction.Name = "Output";
            await DictionaryManager.SaveRecordAsync(direction);
        }

        var taxRows = await DictionaryManager.GetRecordsAsync<TaxSettings>(null, 1);
        var taxSettings = taxRows.Count > 0 ? taxRows[0] : DictionaryManager.NewRecord<TaxSettings>();
        taxSettings.DefaultTaxCode = vatCode.Code;
        taxSettings.PricesIncludeTax = false;
        await DictionaryManager.SaveRecordAsync(taxSettings);

        var uaRows = await DictionaryManager.GetRecordsAsync<LocalizationUkraineSettings>(null, 1);
        var ua = uaRows.Count > 0 ? uaRows[0] : DictionaryManager.NewRecord<LocalizationUkraineSettings>();
        ua.SingleTaxCode3 = three.Code;
        ua.SingleTaxCode5 = five.Code;
        ua.SingleTaxCodeExcess = over.Code;
        await DictionaryManager.SaveRecordAsync(ua);
    }

    private async Task<TaxCode> EpCodeAsync(Tax ep, string band, decimal rate, DateTime from)
    {
        var category = DictionaryManager.NewRecord<TaxCategory>();
        category.Tax = ep.MetaId;
        category.Code = $"{band}-{Db.NewId():N}"[..10];
        category.Treatment = "STANDARD";
        category = await DictionaryManager.SaveRecordAsync(category);

        var epRate = DictionaryManager.NewRecord<TaxRate>();
        epRate.Tax = ep.MetaId;
        epRate.TaxCategory = category.MetaId;
        epRate.Code = $"{band}R-{Db.NewId():N}"[..10];
        epRate.Rate = rate;
        epRate.EffectiveFrom = from;
        epRate = await DictionaryManager.SaveRecordAsync(epRate);

        var code = DictionaryManager.NewRecord<TaxCode>();
        code.Code = $"{band}C-{Db.NewId():N}"[..10];
        code.Name = $"Single tax {band}";
        code.Tax = ep.MetaId;
        code.TaxCategory = category.MetaId;
        code.TaxRate = epRate.MetaId;
        code.EffectiveFrom = from;
        return await DictionaryManager.SaveRecordAsync(code);
    }

    private async Task PayAsync(Setup s, decimal amount, DateTime on)
    {
        var pay = await DocumentManager.NewDocumentAsync<CustomerPayment>();
        pay.LegalEntity = s.LegalEntity;
        pay.DocumentDate = on;
        pay.Lines.Add(new CustomerPaymentLinesTablePartRow
        {
            Customer = s.Customer,
            Contract = s.Contract,
            Amount = amount,
        });
        await DocumentManager.SaveDocumentAsync(pay);
        pay.Subtype = CustomerPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(pay);
    }

    private static async Task<List<UaRegimeChange>> ChangesAsync(Setup s)
        => await GetService<IDictionaryManager<UaRegimeChange>>()
            .GetRecordsAsync($"LegalEntity = '{s.LegalEntity}'");

    private static async Task<int> RegimeOfAsync(Setup s)
        => (int)await FirstEvent.RegimeOfAsync(s.LegalEntity);

    // ── дата перехода ────────────────────────────────────────────────────────

    [IntegrationTest("Дата перехода — первое число месяца ПОСЛЕ квартала превышения")]
    public void NextQuarterStartIsTheFirstOfTheMonthAfterTheQuarter()
    {
        // Ровно то место, где проще всего ошибиться и взять следующий месяц.
        Assert.IsTrue(FirstEvent.NextQuarterStart(new DateTime(2026, 5, 5)) == new DateTime(2026, 7, 1),
            "5 мая — II квартал, переход с 1 июля, факт {0}",
            FirstEvent.NextQuarterStart(new DateTime(2026, 5, 5)));

        Assert.IsTrue(FirstEvent.NextQuarterStart(new DateTime(2026, 1, 1)) == new DateTime(2026, 4, 1),
            "1 января — I квартал, переход с 1 апреля, факт {0}",
            FirstEvent.NextQuarterStart(new DateTime(2026, 1, 1)));

        // Конец года обязан перевалить в следующий.
        Assert.IsTrue(FirstEvent.NextQuarterStart(new DateTime(2026, 12, 20)) == new DateTime(2027, 1, 1),
            "20 декабря — IV квартал, переход с 1 января следующего года, факт {0}",
            FirstEvent.NextQuarterStart(new DateTime(2026, 12, 20)));
    }

    // ── запись ───────────────────────────────────────────────────────────────

    [IntegrationTest("Превышение записывает РОВНО одну строку перехода")]
    public async Task ExcessSchedulesExactlyOneChange()
    {
        var s = await SetupAsync(UaTaxRegime.SimplifiedNoVat, limit: 1000m);
        var day = new DateTime(2026, 5, 5);

        await PayAsync(s, 800m, day);
        Assert.IsTrue((await ChangesAsync(s)).Count == 0,
            "под пределом перехода быть не должно, факт {0}", (await ChangesAsync(s)).Count);

        await PayAsync(s, 500m, day);
        var rows = await ChangesAsync(s);
        Assert.IsTrue(rows.Count == 1, "одно превышение — одна строка, факт {0}", rows.Count);
        Assert.IsTrue(rows[0].EffectiveFrom.Date == new DateTime(2026, 7, 1),
            "переход с 1 июля, факт {0}", rows[0].EffectiveFrom.Date);
        Assert.IsTrue(rows[0].AppliedOn is null, "переход ещё не применён");

        // Дальнейшие оплаты того же квартала НЕ плодят строк: превышение уже
        // зафиксировано, а три записи об одном переходе — это не аудит, а мусор.
        await PayAsync(s, 300m, day);
        Assert.IsTrue((await ChangesAsync(s)).Count == 1,
            "повторное превышение не добавляет строк, факт {0}", (await ChangesAsync(s)).Count);
    }

    // ── применение ───────────────────────────────────────────────────────────

    [IntegrationTest("До даты перехода задание режим не трогает")]
    public async Task NothingHappensBeforeTheDate()
    {
        var s = await SetupAsync(UaTaxRegime.SimplifiedNoVat, limit: 1000m);
        await PayAsync(s, 1500m, new DateTime(2026, 5, 5));

        var applied = await FirstEvent.ApplyDueRegimeChangesAsync(new DateTime(2026, 6, 30));
        Assert.IsTrue(applied == 0, "30 июня переход ещё не наступил, факт {0}", applied);
        Assert.IsTrue(await RegimeOfAsync(s) == (int)UaTaxRegime.SimplifiedNoVat,
            "режим обязан остаться спрощеним, факт {0}", await RegimeOfAsync(s));
    }

    [IntegrationTest("С даты перехода спрощенець 5% становится НЕплательщиком на общей системе")]
    public async Task FivePercentBecomesGeneralNonPayer()
    {
        var s = await SetupAsync(UaTaxRegime.SimplifiedNoVat, limit: 1000m);
        await PayAsync(s, 1500m, new DateTime(2026, 5, 5));

        var applied = await FirstEvent.ApplyDueRegimeChangesAsync(new DateTime(2026, 7, 1));
        Assert.IsTrue(applied == 1, "переведено одно юрлицо, факт {0}", applied);
        Assert.IsTrue(await RegimeOfAsync(s) == (int)UaTaxRegime.General,
            "5% не был плательщиком ПДВ и им не становится, факт {0}", await RegimeOfAsync(s));

        // Повторный прогон задания ничего не делает: AppliedOn — предохранитель.
        Assert.IsTrue(await FirstEvent.ApplyDueRegimeChangesAsync(new DateTime(2026, 7, 2)) == 0,
            "повторный прогон не переводит второй раз");
    }

    [IntegrationTest("Спрощенець 3% был плательщиком ПДВ и остаётся им на общей системе")]
    public async Task ThreePercentStaysVatPayer()
    {
        var s = await SetupAsync(UaTaxRegime.SimplifiedWithVat, limit: 1000m);
        // Деньги с ПДВ: 1800 брутто при 20% дают базу ЄП 1500.
        await PayAsync(s, 1800m, new DateTime(2026, 5, 5));

        var rows = await ChangesAsync(s);
        Assert.IsTrue(rows.Count == 1, "превышение зафиксировано, факт {0}", rows.Count);

        await FirstEvent.ApplyDueRegimeChangesAsync(new DateTime(2026, 7, 1));
        Assert.IsTrue(await RegimeOfAsync(s) == (int)UaTaxRegime.VatPayer,
            "регистрация плательщиком ПДВ переходом не отменяется, факт {0}", await RegimeOfAsync(s));
    }
}
