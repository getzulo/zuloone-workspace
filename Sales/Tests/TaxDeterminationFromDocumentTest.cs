using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// ═══ ОПРЕДЕЛЕНИЕ НАЛОГА ИЗ ДОКУМЕНТА ═════════════════════════════════════════
//
// ЗАЧЕМ ЭТОТ ФАЙЛ СУЩЕСТВУЕТ. TaxProfileMappingTest доказывает, что движок
// определения работает — и не может доказать, что до него кто-то доходит: он
// зовёт сервис напрямую и КОНТЕКСТ ПОДАЁТ САМ:
//
//     Svc.CreateCalculationAsync(le, "OUTPUT", 1000m, …, Ctx(("buyer.id", party)))
//
// А решает всё именно контекст: при `context == null` ResolveDeterminationAsync
// первой же строкой пропускает и правила, и сопоставления, и берёт
// DefaultTaxCode. Счёт-фактура контекст собирала, а кредит-нота, дебет-нота,
// приход и возврат поставщику — нет. То есть счёт определялся по сопоставлению,
// а СТОРНО ТОГО ЖЕ СЧЁТА — по умолчанию.
//
// Здесь документы проводятся по-настоящему, и цифры сходятся только если
// контекст доехал.
public class TaxDeterminationFromDocumentTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid LegalEntity;
        public Guid Customer;
        public Guid Outlet;
        public Guid Contract;
        public Guid DefaultCode;   // 20%, стоит в настройках
        public Guid MappedCode;    // 10%, привязан сопоставлением к юрлицу
    }

    private async Task<Setup> SetupAsync(bool mapLegalEntity = true)
    {
        await Db.SetAccountingPeriodsAsync(null, null);

        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Currency";
        currency.Code = $"C{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "¤";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Country";
        country.CodeISO2 = $"{Db.NewId():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "000";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "Seller";
        legalEntity.RegistrationNumber = $"REG-{Db.NewId():N}"[..16];
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
        store.Name = "WH";
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
        customer.Name = "Buyer";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var codes = await TaxCircuitAsync(legalEntity.MetaId, mapLegalEntity);

        var outlet = DictionaryManager.NewRecord<CustomerOutlet>();
        outlet.Name = "Outlet";
        outlet.Customer = customer.MetaId;
        outlet = await DictionaryManager.SaveRecordAsync(outlet);

        var contract = DictionaryManager.NewRecord<SalesContract>();
        contract.Name = "C-2026";
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
            Customer = customer.MetaId,
            Outlet = outlet.MetaId,
            Contract = contract.MetaId,
            DefaultCode = codes.Default,
            MappedCode = codes.Mapped,
        };
    }

    /// <summary>
    /// Два кода одного налога в РАЗНЫХ категориях: 20% стоит умолчанием в
    /// настройках, 10% привязывается сопоставлением к юрлицу. Категории обязаны
    /// быть разными — две ставки одного налога в одной категории разрешаются в
    /// «действует больше одной ставки», и определение отказало бы ещё до того,
    /// как стало бы видно, какой код выбран.
    /// </summary>
    private async Task<(Guid Default, Guid Mapped)> TaxCircuitAsync(Guid legalEntity, bool map)
    {
        var from = new DateTime(2020, 1, 1);

        var authority = DictionaryManager.NewRecord<TaxAuthority>();
        authority.Code = $"AU-{Db.NewId():N}"[..10];
        authority.Name = "Authority";
        authority.CountryCode = "XX";
        authority.IsActive = true;
        authority = await DictionaryManager.SaveRecordAsync(authority);

        var jurisdiction = DictionaryManager.NewRecord<TaxJurisdiction>();
        jurisdiction.Code = $"JU-{Db.NewId():N}"[..10];
        jurisdiction.Name = "Jurisdiction";
        jurisdiction.CountryCode = "XX";
        jurisdiction.Level = 0;
        jurisdiction = await DictionaryManager.SaveRecordAsync(jurisdiction);

        var tax = DictionaryManager.NewRecord<Tax>();
        tax.Code = $"TX-{Db.NewId():N}"[..10];
        tax.Name = "Tax";
        tax.Authority = authority.MetaId;
        tax.Jurisdiction = jurisdiction.MetaId;
        tax.EffectiveFrom = from;
        tax = await DictionaryManager.SaveRecordAsync(tax);

        var standard = await CodeAsync(tax, "STD", 0.20m, from);
        var reduced = await CodeAsync(tax, "RED", 0.10m, from);

        if ((await DictionaryManager.GetRecordsAsync<TaxDirection>("Code = 'OUTPUT'", take: 1)).Count == 0)
        {
            var direction = DictionaryManager.NewRecord<TaxDirection>();
            direction.Code = "OUTPUT";
            direction.Name = "Output";
            await DictionaryManager.SaveRecordAsync(direction);
        }

        var rows = await DictionaryManager.GetRecordsAsync<TaxSettings>(null, 1);
        var settings = rows.Count > 0 ? rows[0] : DictionaryManager.NewRecord<TaxSettings>();
        settings.DefaultTaxCode = standard.Code;
        settings.PricesIncludeTax = false;
        await DictionaryManager.SaveRecordAsync(settings);

        if (map)
        {
            var mapping = DictionaryManager.NewRecord<TaxMapping>();
            mapping.SourceType = "LegalEntity";
            mapping.SourceId = legalEntity;
            mapping.TaxCode = reduced.MetaId;
            mapping.Priority = 10;
            mapping.EffectiveFrom = from;
            await DictionaryManager.SaveRecordAsync(mapping);
        }

        return (standard.MetaId, reduced.MetaId);
    }

    private async Task<TaxCode> CodeAsync(Tax tax, string band, decimal rate, DateTime from)
    {
        var category = DictionaryManager.NewRecord<TaxCategory>();
        category.Tax = tax.MetaId;
        category.Code = $"{band}-{Db.NewId():N}"[..10];
        category.Treatment = "STANDARD";
        category = await DictionaryManager.SaveRecordAsync(category);

        var taxRate = DictionaryManager.NewRecord<TaxRate>();
        taxRate.Tax = tax.MetaId;
        taxRate.TaxCategory = category.MetaId;
        taxRate.Code = $"{band}R-{Db.NewId():N}"[..10];
        taxRate.Rate = rate;
        taxRate.EffectiveFrom = from;
        taxRate = await DictionaryManager.SaveRecordAsync(taxRate);

        var code = DictionaryManager.NewRecord<TaxCode>();
        code.Code = $"{band}C-{Db.NewId():N}"[..10];
        code.Name = $"Code {band}";
        code.Tax = tax.MetaId;
        code.TaxCategory = category.MetaId;
        code.TaxRate = taxRate.MetaId;
        code.EffectiveFrom = from;
        return await DictionaryManager.SaveRecordAsync(code);
    }

    private static Task<decimal> LedgerAsync(Setup s, Guid code)
        => TotalsManager.GetBalanceAsync("TaxLedger", "TaxAmount",
            new Dictionary<string, object?>
            {
                ["LegalEntity"] = s.LegalEntity,
                ["TaxCode"] = code,
            });

    private async Task StockAsync(Setup s)
        => await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 50m });

    private async Task<SalesRealization> IssueAsync(Setup s)
    {
        var invoice = await DocumentManager.NewDocumentAsync<SalesRealization>();
        invoice.Customer = s.Customer;
        invoice.Outlet = s.Outlet;
        invoice.Contract = s.Contract;
        invoice.Location = s.Location;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 10m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(invoice);
        invoice.Subtype = SalesRealization.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);
        return (await DocumentManager.GetDocumentAsync<SalesRealization>(invoice.MetaId))!;
    }

    private async Task CreditAsync(Setup s, SalesRealization invoice)
    {
        var note = await DocumentManager.NewDocumentAsync<SalesCreditNote>();
        note.Customer = s.Customer;
        note.Outlet = s.Outlet;
        note.Contract = s.Contract;
        note.OriginalInvoice = invoice.MetaId;
        await DocumentManager.SaveDocumentAsync(note);

        var commandId = await Db.FindCommandIdAsync("document", "PostSalesCreditNote");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, note.MetaId);
        Assert.IsTrue(run.Success, "PostSalesCreditNote: {0}", run.Message ?? "");
    }

    [IntegrationTest("Сопоставление на юрлице бьёт умолчание при выставлении счёта")]
    public async Task InvoiceHonoursLegalEntityMapping()
    {
        var s = await SetupAsync();
        await StockAsync(s);
        await IssueAsync(s);

        // База 100. Привязанный код — 10%, умолчание — 20%.
        Assert.IsTrue(await LedgerAsync(s, s.MappedCode) == 10m,
            "счёт обязан определиться по сопоставлению (10), факт {0}",
            await LedgerAsync(s, s.MappedCode));
        Assert.IsTrue(await LedgerAsync(s, s.DefaultCode) == 0m,
            "по коду умолчания не должно быть ничего, факт {0}",
            await LedgerAsync(s, s.DefaultCode));
    }

    [IntegrationTest("Кредит-нота сторнирует ТЕМ ЖЕ кодом, что и счёт, и леджер сходится в ноль")]
    public async Task CreditNoteReversesWithTheSameCode()
    {
        var s = await SetupAsync();
        await StockAsync(s);
        var invoice = await IssueAsync(s);
        Assert.IsTrue(await LedgerAsync(s, s.MappedCode) == 10m,
            "счёт начислил 10, факт {0}", await LedgerAsync(s, s.MappedCode));

        await CreditAsync(s, invoice);

        // ВОТ РАДИ ЧЕГО ТЕСТ. Пока кредит-нота не передавала контекст, она
        // сторнировала по DefaultTaxCode: счёт клал +10 по привязанному коду, а
        // сторно снимало −20 по коду умолчания. Возврат целого счёта оставлял
        // леджер в минусе на 10 из ничего.
        Assert.IsTrue(await LedgerAsync(s, s.MappedCode) == 0m,
            "привязанный код обязан сойтись в ноль, факт {0}",
            await LedgerAsync(s, s.MappedCode));
        Assert.IsTrue(await LedgerAsync(s, s.DefaultCode) == 0m,
            "по коду умолчания сторно проходить НЕ должно, факт {0}",
            await LedgerAsync(s, s.DefaultCode));
    }

    [IntegrationTest("Без сопоставления счёт по-прежнему берёт код из настроек")]
    public async Task WithoutMappingDefaultStillApplies()
    {
        var s = await SetupAsync(mapLegalEntity: false);
        await StockAsync(s);
        await IssueAsync(s);

        // Обратная половина: подача контекста НЕ должна ломать стенды, где
        // сопоставлений нет вовсе — там умолчание обязано работать как прежде.
        Assert.IsTrue(await LedgerAsync(s, s.DefaultCode) == 20m,
            "без сопоставления действует умолчание 20, факт {0}",
            await LedgerAsync(s, s.DefaultCode));
        Assert.IsTrue(await LedgerAsync(s, s.MappedCode) == 0m,
            "непривязанный код не должен использоваться, факт {0}",
            await LedgerAsync(s, s.MappedCode));
    }
}
