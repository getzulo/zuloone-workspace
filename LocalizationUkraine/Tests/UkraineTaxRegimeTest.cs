using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// ═══ РЕЖИМЫ НАЛОГООБЛОЖЕНИЯ УКРАИНЫ ══════════════════════════════════════════
//
// Пакет обязан обслуживать ОДНУ систему, в которой живут ТОВ и два ФОПа, каждый
// на своём режиме. Режим — свойство юрлица, поэтому юрлицо стало измерением
// UaVatFirstEvent, а драйвер по нему ветвится:
//
//   Платник ПДВ          ПДВ с max(отгружено, оплачено). ЄП нет.
//   Спрощенець 3% з ПДВ  ОБА налога, причём ЄП берётся с дохода БЕЗ ПДВ.
//   Спрощенець 5% без ПДВ Только ЄП, и со ВСЕЙ полученной суммы. ПДВ нет.
//   Загальна без ПДВ     Ни того, ни другого.
//
// ЧТО ЗДЕСЬ ЛЕГКО СЛОМАТЬ И НЕ ЗАМЕТИТЬ. Драйвер молчит во всех отказах: нет
// ставки — нет начисления, нет кода — нет начисления. Поэтому почти каждый тест
// проверяет не только «начислилось сколько надо», но и ВТОРУЮ величину —
// что соседний налог НЕ начислился. Без этого «ЄП = 0» у платника ПДВ выглядит
// правильно и тогда, когда ЄП не считается вообще ни у кого.
public class UkraineTaxRegimeTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid LegalEntity;
        public Guid Currency;
        public Guid Customer;
        public Guid Outlet;
        public Guid Contract;
        public Guid SingleTax3;
        public Guid SingleTax5;
    }

    // ── контур ───────────────────────────────────────────────────────────────

    private async Task<Setup> SetupAsync(UaTaxRegime regime)
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
        legalEntity.Name = "Kyiv Trading";
        legalEntity.RegistrationNumber = $"REG-UA-{Db.NewId():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        // Режим — поле РАСШИРЕНИЯ чужого справочника: в типизированный класс
        // LegalEntity оно не попадает, пишется мешком по имени.
        //
        // ЧИСЛОМ, А НЕ ИМЕНЕМ. Через REST перечисление переезжает по имени, но
        // Db.UpdateAsync идёт прямо в DataService, минуя разбор имён, и колонка
        // там целочисленная: строка «VatPayer» падает как «Conversion failed
        // when converting the nvarchar value to data type int».
        //
        // Обязательные поля переданы повторно — адресное обновление переписывает строку.
        await Db.UpdateAsync("LegalEntity", legalEntity.MetaId,
            new Dictionary<string, object?>
            {
                ["UaTaxRegime"] = (int)regime,
                ["Country"] = country.MetaId,
                ["Currency"] = currency.MetaId,
            });

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"SP{Db.NewId():N}"[..8];
        divisionType.Name = "SalesPoint";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Shop";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Shop WH";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "Zone";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"PICK-{Db.NewId():N}"[..12];
        cellType.Name = "Picking";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "P-01";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await DictionaryManager.SaveRecordAsync(cell);

        var unit = DictionaryManager.NewRecord<UnitOfMeasure>();
        unit.Name = "Piece";
        unit.Code = $"P{Db.NewId():N}"[..8];
        unit = await DictionaryManager.SaveRecordAsync(unit);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G{Db.NewId():N}"[..8];
        group.Name = "Goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Widget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = unit.MetaId;
        item.IsSellable = true;
        item.Image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var epCodes = await TaxCircuitAsync();

        var outlet = DictionaryManager.NewRecord<CustomerOutlet>();
        outlet.Name = "Shop A";
        outlet.Customer = customer.MetaId;
        outlet = await DictionaryManager.SaveRecordAsync(outlet);

        var contract = DictionaryManager.NewRecord<SalesContract>();
        contract.Name = "UA-2026";
        contract.Outlet = outlet.MetaId;
        contract.Currency = currency.MetaId;
        contract.SettlementKind = SettlementKind.Credit;
        contract.EffectiveFrom = new DateTime(2020, 1, 1);
        contract.LegalEntity = legalEntity.MetaId;
        contract = await DictionaryManager.SaveRecordAsync(contract);

        return new Setup
        {
            Location = cell.MetaId,
            Item = item.MetaId,
            LegalEntity = legalEntity.MetaId,
            Currency = currency.MetaId,
            Customer = customer.MetaId,
            Outlet = outlet.MetaId,
            Contract = contract.MetaId,
            SingleTax3 = epCodes.Three,
            SingleTax5 = epCodes.Five,
        };
    }

    /// <summary>
    /// ПДВ 20% и единый налог 3% / 5%. КАЖДАЯ ставка ЄП живёт в своей категории:
    /// две ставки одного налога без категорий разрешаются в «действует больше
    /// одной ставки», драйвер этот отказ глотает, и ЄП молча не начисляется.
    /// </summary>
    private async Task<(Guid Three, Guid Five)> TaxCircuitAsync()
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

        // ── ПДВ ──
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

        // ── ЄДИНИЙ ПОДАТОК ──
        var ep = DictionaryManager.NewRecord<Tax>();
        ep.Code = $"EP-{Db.NewId():N}"[..10];
        ep.Name = "Ukraine single tax";
        ep.Authority = authority.MetaId;
        ep.Jurisdiction = jurisdiction.MetaId;
        ep.EffectiveFrom = from;
        ep = await DictionaryManager.SaveRecordAsync(ep);

        var code3 = await SingleTaxCodeAsync(ep, "EP3", 0.03m, from);
        var code5 = await SingleTaxCodeAsync(ep, "EP5", 0.05m, from);

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
        var uaSettings = uaRows.Count > 0 ? uaRows[0] : DictionaryManager.NewRecord<LocalizationUkraineSettings>();
        uaSettings.SingleTaxCode3 = code3.Code;
        uaSettings.SingleTaxCode5 = code5.Code;
        await DictionaryManager.SaveRecordAsync(uaSettings);

        return (code3.MetaId, code5.MetaId);
    }

    private async Task<TaxCode> SingleTaxCodeAsync(Tax ep, string band, decimal rate, DateTime from)
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

    // ── замеры ───────────────────────────────────────────────────────────────

    private static Task<decimal> VatAsync(Setup s)
        => TotalsManager.GetBalanceAsync("UaVatPayable", "Amount",
            new Dictionary<string, object?>
            {
                ["Customer"] = s.Customer,
                ["CustomerOutlet"] = s.Outlet,
            });

    // СРЕЗ ОБЯЗАТЕЛЬНО ПО КОДУ НАЛОГА, а не по одному юрлицу. В TaxLedger пишет не
    // только этот пакет: выставление счёта заводит налоговый расчёт OUTPUT через
    // Sales → ITaxService, и ПДВ лежит в том же леджере того же юрлица. Срез по
    // юрлицу целиком мерил бы сумму двух налогов — на этом и упали первые прогоны
    // («платник ПДВ не платит ЄП, факт 20.00»: 20 — это ПДВ, а не ЄП).
    private static Task<decimal> LedgerAsync(Setup s, Guid code, string resource)
        => TotalsManager.GetBalanceAsync("TaxLedger", resource,
            new Dictionary<string, object?>
            {
                ["LegalEntity"] = s.LegalEntity,
                ["TaxCode"] = code,
            });

    private static async Task<decimal> EpAmountAsync(Setup s)
        => await LedgerAsync(s, s.SingleTax3, "TaxAmount")
         + await LedgerAsync(s, s.SingleTax5, "TaxAmount");

    private static async Task<decimal> EpBaseAsync(Setup s)
        => await LedgerAsync(s, s.SingleTax3, "TaxBase")
         + await LedgerAsync(s, s.SingleTax5, "TaxBase");

    private async Task StockAsync(Setup s)
        => await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 20m });

    private async Task<SalesRealization> IssueAsync(Setup s, decimal qty, decimal price)
    {
        var invoice = await DocumentManager.NewDocumentAsync<SalesRealization>();
        invoice.Customer = s.Customer;
        invoice.Outlet = s.Outlet;
        invoice.Contract = s.Contract;
        invoice.Location = s.Location;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(invoice);
        invoice.Subtype = SalesRealization.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);
        return (await DocumentManager.GetDocumentAsync<SalesRealization>(invoice.MetaId))!;
    }

    private async Task<CustomerPayment> PayAsync(Setup s, decimal amount, Guid? entity = null)
    {
        var pay = await DocumentManager.NewDocumentAsync<CustomerPayment>();
        pay.LegalEntity = entity ?? s.LegalEntity;
        pay.Lines.Add(new CustomerPaymentLinesTablePartRow
        {
            Customer = s.Customer,
            Contract = s.Contract,
            Amount = amount,
        });
        await DocumentManager.SaveDocumentAsync(pay);
        pay.Subtype = CustomerPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(pay);
        return pay;
    }

    // ── режимы ───────────────────────────────────────────────────────────────

    [IntegrationTest("Платник ПДВ: налог по первому событию, единого налога нет")]
    public async Task VatPayerAccruesVatOnly()
    {
        var s = await SetupAsync(UaTaxRegime.VatPayer);
        await StockAsync(s);

        await IssueAsync(s, 10m, 10m);

        Assert.IsTrue(await VatAsync(s) == 20m,
            "ПДВ 20 при базе 100, факт {0}", await VatAsync(s));
        // Вторая половина утверждения: плательщик ПДВ единый налог НЕ платит.
        Assert.IsTrue(await EpAmountAsync(s) == 0m,
            "платник ПДВ не платит ЄП, факт {0}", await EpAmountAsync(s));
    }

    [IntegrationTest("Спрощенець 5%: ПДВ не начисляется вовсе, ЄП берётся со всей полученной суммы")]
    public async Task SimplifiedNoVatPaysSingleTaxOnGross()
    {
        var s = await SetupAsync(UaTaxRegime.SimplifiedNoVat);
        await StockAsync(s);

        // Не плательщик ПДВ: в деньгах налога нет, вся сотня — доход.
        await PayAsync(s, 100m);

        Assert.IsTrue(await VatAsync(s) == 0m,
            "спрощенець без ПДВ не начисляет ПДВ, факт {0}", await VatAsync(s));
        Assert.IsTrue(await EpBaseAsync(s) == 100m,
            "база ЄП — ВСЯ полученная сумма 100, а не очищенная от ПДВ: факт {0}",
            await EpBaseAsync(s));
        Assert.IsTrue(await EpAmountAsync(s) == 5m,
            "ЄП 5% со 100 = 5, факт {0}", await EpAmountAsync(s));
    }

    [IntegrationTest("Спрощенець 5%: отгрузка без денег не облагается — база кассовая")]
    public async Task SimplifiedNoVatIgnoresShipment()
    {
        var s = await SetupAsync(UaTaxRegime.SimplifiedNoVat);
        await StockAsync(s);

        // ЄП платится С ПОЛУЧЕННОГО ДОХОДА. Отгрузка в долг дохода не создаёт, и
        // это не мелочь: возьми драйвер max(Shipped, Paid), как для ПДВ, — и
        // спрощенець заплатил бы налог за неоплаченную поставку.
        await IssueAsync(s, 10m, 10m);

        Assert.IsTrue(await EpAmountAsync(s) == 0m,
            "отгрузка без оплаты ЄП не рождает, факт {0}", await EpAmountAsync(s));
        Assert.IsTrue(await VatAsync(s) == 0m,
            "и ПДВ тоже нет, факт {0}", await VatAsync(s));

        // А деньги — рождают, и ровно за то, что оплачено.
        await PayAsync(s, 100m);
        Assert.IsTrue(await EpAmountAsync(s) == 5m,
            "после оплаты 100 ЄП = 5, факт {0}", await EpAmountAsync(s));
    }

    [IntegrationTest("Спрощенець 3%: платит оба налога, причём ЄП с дохода БЕЗ ПДВ")]
    public async Task SimplifiedWithVatPaysBoth()
    {
        var s = await SetupAsync(UaTaxRegime.SimplifiedWithVat);
        await StockAsync(s);

        // 120 брутто при ставке 20% = база 100.
        await PayAsync(s, 120m);

        Assert.IsTrue(await VatAsync(s) == 20m,
            "ПДВ по первому событию 20, факт {0}", await VatAsync(s));
        Assert.IsTrue(await EpBaseAsync(s) == 100m,
            "база ЄП — доход БЕЗ ПДВ, то есть 100, а не 120: факт {0}", await EpBaseAsync(s));
        Assert.IsTrue(await EpAmountAsync(s) == 3m,
            "ЄП 3% со 100 = 3, факт {0}", await EpAmountAsync(s));
    }

    [IntegrationTest("Загальна без ПДВ: не начисляется ни ПДВ, ни единый налог")]
    public async Task GeneralWithoutVatAccruesNothing()
    {
        var s = await SetupAsync(UaTaxRegime.General);
        await StockAsync(s);

        await IssueAsync(s, 10m, 10m);
        await PayAsync(s, 100m);

        Assert.IsTrue(await VatAsync(s) == 0m,
            "не плательщик ПДВ — ПДВ нет, факт {0}", await VatAsync(s));
        Assert.IsTrue(await EpAmountAsync(s) == 0m,
            "не спрощенець — ЄП нет, факт {0}", await EpAmountAsync(s));
    }

    [IntegrationTest("Единый налог не начисляется повторно за уже обложенные деньги")]
    public async Task SingleTaxChargesOnlyTheIncrement()
    {
        var s = await SetupAsync(UaTaxRegime.SimplifiedNoVat);
        await StockAsync(s);

        await PayAsync(s, 100m);
        Assert.IsTrue(await EpAmountAsync(s) == 5m,
            "первая оплата 100 даёт ЄП 5, факт {0}", await EpAmountAsync(s));

        // Вторая оплата облагается ТОЛЬКО сама: EpAccrued помнит первую.
        await PayAsync(s, 200m);
        Assert.IsTrue(await EpBaseAsync(s) == 300m,
            "суммарная база ЄП 300, факт {0}", await EpBaseAsync(s));
        Assert.IsTrue(await EpAmountAsync(s) == 15m,
            "ЄП 5% с 300 = 15, а не 20 (двойное обложение первой сотни): факт {0}",
            await EpAmountAsync(s));
    }

    // ── защита координаты ────────────────────────────────────────────────────

    [IntegrationTest("Оплата с чужим юрлицом в шапке отклоняется, а не начисляет налог дважды")]
    public async Task PaymentWithForeignLegalEntityIsRejected()
    {
        var s = await SetupAsync(UaTaxRegime.VatPayer);
        await StockAsync(s);

        // Соседний ФОП в той же системе — ровно тот случай, ради которого пакет и
        // переделывался.
        var other = DictionaryManager.NewRecord<LegalEntity>();
        other.Name = "Sole trader";
        other.RegistrationNumber = $"REG-FOP-{Db.NewId():N}"[..16];
        other.Country = (await DictionaryManager.GetRecordAsync<LegalEntity>(s.LegalEntity))!.Country;
        other.Currency = s.Currency;
        other = await DictionaryManager.SaveRecordAsync(other);

        await IssueAsync(s, 10m, 10m);
        Assert.IsTrue(await VatAsync(s) == 20m, "отгрузка начислила 20, факт {0}", await VatAsync(s));

        var failed = false;
        try
        {
            await PayAsync(s, 120m, entity: other.MetaId);
        }
        catch (Exception)
        {
            failed = true;
        }

        Assert.IsTrue(failed,
            "оплата с юрлицом, не совпадающим с юрлицом договора, обязана быть отклонена");

        // И главное — налог остался начисленным ОДИН раз. Без защиты Paid лёг бы в
        // координату соседнего ФОПа, max(Shipped, Paid) там посчитался бы с нуля,
        // и ПДВ стал бы 40 при одной поставке на 100.
        var after = await VatAsync(s);
        Assert.IsTrue(after == 20m,
            "ПДВ обязан остаться 20, а не удвоиться: факт {0}", after);
    }

    [IntegrationTest("Оплата без юрлица в шапке достаёт его из договора, а не двоит налог")]
    public async Task PaymentWithoutLegalEntityIsStampedFromContract()
    {
        var s = await SetupAsync(UaTaxRegime.VatPayer);
        await StockAsync(s);

        await IssueAsync(s, 10m, 10m);
        Assert.IsTrue(await VatAsync(s) == 20m, "отгрузка начислила 20, факт {0}", await VatAsync(s));

        // Пустое юрлицо — не спор с договором, а отсутствие данных: отклонять
        // нечего, надо подставить. Оставь его пустым — и Paid уйдёт в пустую
        // координату мимо Shipped, а налог начислится второй раз.
        await PayAsync(s, 120m, entity: Guid.Empty);

        var after = await VatAsync(s);
        Assert.IsTrue(after == 20m,
            "оплата после отгрузки — вторая подія, налог не повторяется: ждали 20, факт {0}", after);
    }
}
