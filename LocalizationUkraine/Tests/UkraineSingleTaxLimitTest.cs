using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

// ═══ ГОДОВОЙ ПРЕДЕЛ ДОХОДА ЕДИНОГО НАЛОГА (ПКУ 291.4 / 293.4) ════════════════
//
// Превышение предела НЕ запрещено: деньги пришли, отказывать в доходе не за что.
// Закон облагает сумму сверх предела по 15%, поэтому ОДНА оплата, пересекающая
// порог, обязана лечь в леджер ДВУМЯ строками разными кодами — иначе обычная
// ставка и штрафная слипнутся и разложить их в декларации будет нечем.
//
// Предел взят маленьким (1000) намеренно: настоящий — миллионы, и тест на нём
// читался бы как шум. Проверяется механика деления, а не конкретная цифра;
// сама цифра живёт в ДАННЫХ, потому что меняется каждый год.
public class UkraineSingleTaxLimitTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid LegalEntity;
        public Guid Customer;
        public Guid Contract;
        public Guid NormalCode;   // 5%
        public Guid ExcessCode;   // 15%, ставка ФОП
        public Guid DoubleCode;   // 10%, подвійна ставка юрособи
    }

    /// <param name="legalPerson">
    /// Юрособа, а не ФОП. Ставится В ТОМ ЖЕ обновлении, что страна и валюта:
    /// событие юрлица требует их обе, а частичный bag несёт только те колонки,
    /// которые пишут, — отдельный апдейт флага падает «Страна и валюта обязательны».
    /// </param>
    private async Task<Setup> SetupAsync(decimal limit, bool legalPerson = false)
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
        legalEntity.RegistrationNumber = $"REG-LIM-{Db.NewId():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        // Спрощенець 5%: ПДВ в деньгах нет, база ЄП — вся полученная сумма, и
        // арифметика предела читается без поправок на налог.
        await Db.UpdateAsync("LegalEntity", legalEntity.MetaId,
            new Dictionary<string, object?>
            {
                ["UaTaxRegime"] = (int)UaTaxRegime.SimplifiedNoVat,
                ["IsLegalPerson"] = legalPerson,
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
        contract.Name = "UA-LIM";
        contract.Outlet = outlet.MetaId;
        contract.Currency = currency.MetaId;
        contract.SettlementKind = SettlementKind.Credit;
        contract.EffectiveFrom = new DateTime(2020, 1, 1);
        contract.LegalEntity = legalEntity.MetaId;
        contract = await DictionaryManager.SaveRecordAsync(contract);

        var codes = await TaxCircuitAsync();

        if (limit > 0m)
        {
            var row = DictionaryManager.NewRecord<UaSingleTaxLimit>();
            row.Code = $"LIM-{Db.NewId():N}"[..10];
            row.Amount = limit;
            row.EffectiveFrom = new DateTime(2020, 1, 1);
            await DictionaryManager.SaveRecordAsync(row);
        }

        return new Setup
        {
            LegalEntity = legalEntity.MetaId,
            Customer = customer.MetaId,
            Contract = contract.MetaId,
            NormalCode = codes.Normal,
            ExcessCode = codes.Excess,
            DoubleCode = codes.Double,
        };
    }

    private async Task<(Guid Normal, Guid Excess, Guid Double)> TaxCircuitAsync()
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

        var five = await EpCodeAsync(ep, "EP5", 0.05m, from);
        var over = await EpCodeAsync(ep, "E15", 0.15m, from);
        // Подвійна до 5 % (ПКУ 293.5) — ставка превышения у ЮРОСОБИ.
        var twice = await EpCodeAsync(ep, "E10", 0.10m, from);

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
        ua.SingleTaxCode5 = five.Code;
        ua.SingleTaxCodeExcess = over.Code;
        ua.SingleTaxCodeDouble5 = twice.Code;
        await DictionaryManager.SaveRecordAsync(ua);

        return (five.MetaId, over.MetaId, twice.MetaId);
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

    private static Task<decimal> LedgerAsync(Setup s, Guid code, string resource)
        => TotalsManager.GetBalanceAsync("TaxLedger", resource,
            new Dictionary<string, object?>
            {
                ["LegalEntity"] = s.LegalEntity,
                ["TaxCode"] = code,
            });

    private async Task PayAsync(Setup s, decimal amount)
    {
        var pay = await DocumentManager.NewDocumentAsync<CustomerPayment>();
        pay.LegalEntity = s.LegalEntity;
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

    [IntegrationTest("Доход под пределом облагается обычной ставкой целиком")]
    public async Task UnderTheLimitNothingChanges()
    {
        var s = await SetupAsync(limit: 1000m);
        await PayAsync(s, 400m);

        Assert.IsTrue(await LedgerAsync(s, s.NormalCode, "TaxBase") == 400m,
            "вся база идёт обычным кодом, факт {0}", await LedgerAsync(s, s.NormalCode, "TaxBase"));
        Assert.IsTrue(await LedgerAsync(s, s.ExcessCode, "TaxBase") == 0m,
            "превышения нет, факт {0}", await LedgerAsync(s, s.ExcessCode, "TaxBase"));
    }

    [IntegrationTest("Оплата, пересекающая предел, делится на две строки разными кодами")]
    public async Task CrossingTheLimitSplitsOnePayment()
    {
        var s = await SetupAsync(limit: 1000m);

        await PayAsync(s, 800m);
        // 800 под пределом целиком.
        Assert.IsTrue(await LedgerAsync(s, s.NormalCode, "TaxBase") == 800m,
            "первая оплата вся под пределом, факт {0}", await LedgerAsync(s, s.NormalCode, "TaxBase"));

        // Вторая оплата 500 пересекает порог: 200 добирают предел, 300 — сверх.
        await PayAsync(s, 500m);

        Assert.IsTrue(await LedgerAsync(s, s.NormalCode, "TaxBase") == 1000m,
            "обычным кодом ровно предел 1000, факт {0}", await LedgerAsync(s, s.NormalCode, "TaxBase"));
        Assert.IsTrue(await LedgerAsync(s, s.ExcessCode, "TaxBase") == 300m,
            "сверх предела 300, факт {0}", await LedgerAsync(s, s.ExcessCode, "TaxBase"));

        // И суммы налога по своим ставкам: 5% с 1000 и 15% с 300.
        Assert.IsTrue(await LedgerAsync(s, s.NormalCode, "TaxAmount") == 50m,
            "5% с 1000 = 50, факт {0}", await LedgerAsync(s, s.NormalCode, "TaxAmount"));
        Assert.IsTrue(await LedgerAsync(s, s.ExcessCode, "TaxAmount") == 45m,
            "15% с 300 = 45, факт {0}", await LedgerAsync(s, s.ExcessCode, "TaxAmount"));
    }

    [IntegrationTest("Когда предел уже выбран, следующая оплата идёт целиком по 15%")]
    public async Task AboveTheLimitEverythingIsExcess()
    {
        var s = await SetupAsync(limit: 1000m);
        await PayAsync(s, 1000m);
        Assert.IsTrue(await LedgerAsync(s, s.ExcessCode, "TaxBase") == 0m,
            "ровно предел — превышения ещё нет, факт {0}", await LedgerAsync(s, s.ExcessCode, "TaxBase"));

        await PayAsync(s, 200m);
        Assert.IsTrue(await LedgerAsync(s, s.NormalCode, "TaxBase") == 1000m,
            "обычная часть больше не растёт, факт {0}", await LedgerAsync(s, s.NormalCode, "TaxBase"));
        Assert.IsTrue(await LedgerAsync(s, s.ExcessCode, "TaxBase") == 200m,
            "вся вторая оплата — превышение, факт {0}", await LedgerAsync(s, s.ExcessCode, "TaxBase"));
    }

    [IntegrationTest("Юрособа платит превышение подвійною ставкою, а не 15% ФОП")]
    public async Task LegalPersonPaysTheDoubledRate()
    {
        // ЧЕМ ЭТО БЫЛО. Начисление клало UA-EP15 всем подряд, а форма юрособи
        // J0103509 кладёт превышение в рядок 2 подвійною ставкою и UA-EP15 туда
        // не принимает вовсе — сумма падала в «не зіставлено», и декларация
        // молча теряла её. Ставку решает ПРАВОВАЯ ФОРМА, а не режим: у ФОП
        // рядок 07 форми F0103309 так и называется, «за ставкою 15 %».
        var s = await SetupAsync(limit: 1000m, legalPerson: true);
        await PayAsync(s, 1200m);

        // Предел добирается обычной ставкой ровно как у ФОП — различие только в
        // хвосте.
        Assert.IsTrue(await LedgerAsync(s, s.NormalCode, "TaxBase") == 1000m,
            "обычным кодом ровно предел 1000, факт {0}", await LedgerAsync(s, s.NormalCode, "TaxBase"));

        // 200 сверх предела — по подвійній 10 %, то есть 20.
        Assert.IsTrue(await LedgerAsync(s, s.DoubleCode, "TaxBase") == 200m,
            "превышение 200 идёт кодом подвійної, факт {0}", await LedgerAsync(s, s.DoubleCode, "TaxBase"));
        Assert.IsTrue(await LedgerAsync(s, s.DoubleCode, "TaxAmount") == 20m,
            "10% с 200 = 20, факт {0}", await LedgerAsync(s, s.DoubleCode, "TaxAmount"));

        // И главное: ставки ФОП на юрлице не появилось вовсе.
        Assert.IsTrue(await LedgerAsync(s, s.ExcessCode, "TaxBase") == 0m,
            "у юрособи 15% быть не должно, факт {0}", await LedgerAsync(s, s.ExcessCode, "TaxBase"));
    }

    [IntegrationTest("Незаполненный признак — это ФОП, и поведение прежнее")]
    public async Task UnsetFlagKeepsTheSoleProprietorRate()
    {
        // Обратная половина: флаг необязательный и генерится non-nullable, то
        // есть у всех существующих юрлиц он приедет пустым. Пустой ОБЯЗАН
        // означать ровно то, что работало до правки, иначе обновление молча
        // сменит ставку всем. Поэтому поле и названо IsLegalPerson.
        var s = await SetupAsync(limit: 1000m);
        await PayAsync(s, 1200m);

        Assert.IsTrue(await LedgerAsync(s, s.ExcessCode, "TaxBase") == 200m,
            "без признака превышение по-прежнему 15%, факт {0}", await LedgerAsync(s, s.ExcessCode, "TaxBase"));
        Assert.IsTrue(await LedgerAsync(s, s.DoubleCode, "TaxBase") == 0m,
            "подвійної у ФОП быть не должно, факт {0}", await LedgerAsync(s, s.DoubleCode, "TaxBase"));
    }

    [IntegrationTest("Без заданного предела поведение ровно прежнее")]
    public async Task WithoutALimitBehaviourIsUnchanged()
    {
        // Обратная половина: механизм не должен трогать стенды, где предел не
        // заводили, — иначе «улучшение» молча меняет цифры всем.
        var s = await SetupAsync(limit: 0m);
        await PayAsync(s, 5000m);

        Assert.IsTrue(await LedgerAsync(s, s.NormalCode, "TaxBase") == 5000m,
            "без предела вся база обычным кодом, факт {0}", await LedgerAsync(s, s.NormalCode, "TaxBase"));
        Assert.IsTrue(await LedgerAsync(s, s.ExcessCode, "TaxBase") == 0m,
            "и ничего в превышении, факт {0}", await LedgerAsync(s, s.ExcessCode, "TaxBase"));
    }
}
