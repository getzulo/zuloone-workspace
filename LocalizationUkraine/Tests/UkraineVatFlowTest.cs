using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

// Пакет Украины: НДС в UaVatPayable по ставке налогового контура на дату счёта.
// Sales не ветвится. Нет контура — нет проводки (как КСА без TaxRateApplied).
public class UkraineVatFlowTest : IntegrationTestScriptBase
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
    }

    private async Task<Setup> SetupAsync(bool configureTax = true, bool splitRates = false)
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

        if (configureTax)
            await TaxCircuitAsync(splitRates);

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
        };
    }

    private async Task TaxCircuitAsync(bool splitRates)
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

        var tax = DictionaryManager.NewRecord<Tax>();
        tax.Code = $"VT-{Db.NewId():N}"[..10];
        tax.Name = "Ukraine VAT";
        tax.Authority = authority.MetaId;
        tax.Jurisdiction = jurisdiction.MetaId;
        tax.EffectiveFrom = from;
        tax = await DictionaryManager.SaveRecordAsync(tax);

        var rate = DictionaryManager.NewRecord<TaxRate>();
        rate.Tax = tax.MetaId;
        rate.Code = $"R-{Db.NewId():N}"[..10];
        rate.Rate = 0.20m;
        rate.EffectiveFrom = from;
        if (splitRates) rate.EffectiveTo = new DateTime(2025, 12, 31);
        rate = await DictionaryManager.SaveRecordAsync(rate);

        if (splitRates)
        {
            var next = DictionaryManager.NewRecord<TaxRate>();
            next.Tax = tax.MetaId;
            next.Code = $"R2-{Db.NewId():N}"[..10];
            next.Rate = 0.21m;
            next.EffectiveFrom = new DateTime(2026, 1, 1);
            await DictionaryManager.SaveRecordAsync(next);
        }

        var category = DictionaryManager.NewRecord<TaxCategory>();
        category.Tax = tax.MetaId;
        category.Code = $"STD-{Db.NewId():N}"[..10];
        category.Treatment = "STANDARD";
        category = await DictionaryManager.SaveRecordAsync(category);

        var code = DictionaryManager.NewRecord<TaxCode>();
        code.Code = $"OUT-{Db.NewId():N}"[..10];
        code.Name = "Standard 20%";
        code.Tax = tax.MetaId;
        code.TaxCategory = category.MetaId;
        code.TaxRate = rate.MetaId;
        code.EffectiveFrom = from;
        code = await DictionaryManager.SaveRecordAsync(code);

        if ((await DictionaryManager.GetRecordsAsync<TaxDirection>("Code = 'OUTPUT'", take: 1)).Count == 0)
        {
            var direction = DictionaryManager.NewRecord<TaxDirection>();
            direction.Code = "OUTPUT";
            direction.Name = "Output";
            await DictionaryManager.SaveRecordAsync(direction);
        }

        var rows = await DictionaryManager.GetRecordsAsync<TaxSettings>(null, 1);
        var settings = rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<TaxSettings>();
        settings.DefaultTaxCode = code.Code;
        settings.PricesIncludeTax = false;
        await DictionaryManager.SaveRecordAsync(settings);
    }

    private static Task<decimal> VatAsync(Setup s, Guid? outlet = null)
        => TotalsManager.GetBalanceAsync("UaVatPayable", "Amount",
            new Dictionary<string, object?>
            {
                ["Customer"] = s.Customer,
                ["CustomerOutlet"] = outlet ?? s.Outlet,
            });

    private async Task StockAsync(Setup s)
        => await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 20m });

    private async Task<SalesRealization> IssueAsync(Setup s, decimal qty, decimal price, DateTime? onDate = null)
    {
        var invoice = await DocumentManager.NewDocumentAsync<SalesRealization>();
        invoice.Customer = s.Customer;
        invoice.Outlet = s.Outlet;
        invoice.Contract = s.Contract;
        invoice.Location = s.Location;
        if (onDate is DateTime day) invoice.DocumentDate = day;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(invoice);
        invoice.Subtype = SalesRealization.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);
        return (await DocumentManager.GetDocumentAsync<SalesRealization>(invoice.MetaId))!;
    }

    [IntegrationTest("Выставление счёта начисляет ПДВ 20% в UaVatPayable")]
    public async Task IssueAccruesVat()
    {
        var s = await SetupAsync();
        await StockAsync(s);

        var invoice = await DocumentManager.NewDocumentAsync<SalesRealization>();
        invoice.Customer = s.Customer;
        invoice.Outlet = s.Outlet;
        invoice.Contract = s.Contract;
        invoice.Location = s.Location;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 10m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(invoice);
        Assert.IsTrue(await VatAsync(s) == 0m, "черновик не начисляет ПДВ, факт {0}", await VatAsync(s));

        invoice.Subtype = SalesRealization.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);

        var vat = await VatAsync(s);
        Assert.IsTrue(vat == 20m, "ПДВ 20 при базе 100, факт {0}", vat);
    }

    [IntegrationTest("Без налогового контура выставление не пишет UaVatPayable")]
    public async Task NoTaxCircuitPostsNothing()
    {
        var s = await SetupAsync(configureTax: false);
        await StockAsync(s);
        await IssueAsync(s, 10m, 10m);
        Assert.IsTrue(await VatAsync(s) == 0m, "без контура ПДВ 0, факт {0}", await VatAsync(s));
    }

    [IntegrationTest("Счёт задним числом считается по ставке его даты")]
    public async Task BackdatedInvoiceUsesHistoricalRate()
    {
        var s = await SetupAsync(splitRates: true);
        await StockAsync(s);

        var invoice = await IssueAsync(s, 10m, 10m, new DateTime(2024, 6, 1));
        Assert.IsTrue(invoice.TaxRateApplied == 0.20m,
            "ставка 2024-06-01 = 0.20, факт {0}", invoice.TaxRateApplied);
        Assert.IsTrue(await VatAsync(s) == 20m,
            "ПДВ по историческим 20%, не по 21%: факт {0}", await VatAsync(s));
    }

    [IntegrationTest("Кредит-нота сторнирует ПДВ в UaVatPayable")]
    public async Task CreditNoteReversesUaVat()
    {
        var s = await SetupAsync();
        await StockAsync(s);
        var inv = await IssueAsync(s, 10m, 10m);
        Assert.IsTrue(await VatAsync(s) == 20m, "после счёта 20, факт {0}", await VatAsync(s));

        var note = await DocumentManager.NewDocumentAsync<SalesCreditNote>();
        note.Customer = s.Customer;
        note.Outlet = s.Outlet;
        note.Contract = s.Contract;
        note.OriginalInvoice = inv.MetaId;
        await DocumentManager.SaveDocumentAsync(note);

        var commandId = await Db.FindCommandIdAsync("document", "PostSalesCreditNote");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, note.MetaId);
        Assert.IsTrue(run.Success, "PostSalesCreditNote: {0}", run.Message ?? "");
        Assert.IsTrue(await VatAsync(s) == 0m, "после кредит-ноты ПДВ 0, факт {0}", await VatAsync(s));
    }

    [IntegrationTest("ПДВ разных торговых точек одного клиента не смешивается")]
    public async Task OutletSliceIsolated()
    {
        var s = await SetupAsync();
        await StockAsync(s);

        var shopB = DictionaryManager.NewRecord<CustomerOutlet>();
        shopB.Name = "Shop B";
        shopB.Customer = s.Customer;
        shopB = await DictionaryManager.SaveRecordAsync(shopB);

        var contractB = DictionaryManager.NewRecord<SalesContract>();
        contractB.Name = "UA-SHOP-B";
        contractB.Outlet = shopB.MetaId;
        contractB.Currency = s.Currency;
        contractB.SettlementKind = SettlementKind.Credit;
        contractB.EffectiveFrom = new DateTime(2020, 1, 1);
        contractB.LegalEntity = s.LegalEntity;
        contractB = await DictionaryManager.SaveRecordAsync(contractB);

        await IssueAsync(s, 10m, 10m);

        var b = new Setup
        {
            Location = s.Location,
            Item = s.Item,
            LegalEntity = s.LegalEntity,
            Currency = s.Currency,
            Customer = s.Customer,
            Outlet = shopB.MetaId,
            Contract = contractB.MetaId,
        };
        await IssueAsync(b, 5m, 10m);

        Assert.IsTrue(await VatAsync(s) == 20m, "точка A: 20, факт {0}", await VatAsync(s));
        Assert.IsTrue(await VatAsync(b) == 10m, "точка B: 10, факт {0}", await VatAsync(b));
        Assert.IsTrue(await VatAsync(s, shopB.MetaId) == 10m,
            "срез точки B по guid: 10, факт {0}", await VatAsync(s, shopB.MetaId));
    }
}
