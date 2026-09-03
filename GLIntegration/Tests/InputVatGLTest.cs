using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// ВХОДЯЩИЙ НДС ОБЯЗАН СТАТЬ АКТИВОМ В КНИГЕ.
//
// Заказ разносит Dr запасы / Cr кредиторка на сумму БЕЗ налога. Без этой
// проводки книга не знает, что государству можно зачесть входной налог, а
// поставщику должны больше, чем лежит в запасах. Декларация уже вычитает
// входящий из исходящего — книга должна говорить то же.
public class InputVatGLTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    private sealed class Setup
    {
        public Guid Cell;
        public Guid Item;
        public Guid Supplier;
        public Guid PayableAccount;
        public Guid VatAccount;
        public Guid InventoryAccount;
    }

    private async Task<Setup> SetupAsync(bool configureVatAccount = true)
    {
        var today = DateTime.UtcNow.Date;

        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Saudi Riyal";
        currency.Code = "SAR";
        currency.Symbol = "﷼";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Saudi Arabia";
        country.CodeISO2 = "SA";
        country.CodeISO3 = "SAU";
        country.PhoneCode = "966";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "ACME KSA";
        legalEntity.RegistrationNumber = $"REG-IN-{Db.NewId():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"WH-{Db.NewId():N}"[..12];
        divisionType.Name = "Warehouse";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Main";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Central";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "Зона";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"RCV-{Db.NewId():N}"[..12];
        cellType.Name = "Receiving";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "R-01";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await DictionaryManager.SaveRecordAsync(cell);

        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = $"PCS-{Db.NewId():N}"[..12];
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"RAW-{Db.NewId():N}"[..12];
        group.Name = "Raw material";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Bolt";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = uom.MetaId;
        item.IsRawMaterial = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Bolt Supply Co";
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        var inventoryAccount = await NewAccountAsync("1300", "Inventory", AccountType.Asset, currency.MetaId);
        var payableAccount = await NewAccountAsync("2100", "Accounts payable", AccountType.Liability, currency.MetaId);
        var vatAccount = await NewAccountAsync("1400", "VAT receivable", AccountType.Asset, currency.MetaId);

        var accRows = await DictionaryManager.GetRecordsAsync<AccountingSettings>(null, 1);
        var settings = accRows.Count > 0 ? accRows[0] : DictionaryManager.NewRecord<AccountingSettings>();
        settings.InventoryAccountCode = "1300";
        settings.PayableAccountCode = "2100";
        settings.VatReceivableAccountCode = configureVatAccount ? "1400" : null;
        await DictionaryManager.SaveRecordAsync(settings);

        var fiscalYear = DictionaryManager.NewRecord<FiscalYear>();
        fiscalYear.Code = "FY";
        fiscalYear.StartDate = today.AddMonths(-6);
        fiscalYear.EndDate = today.AddMonths(6);
        fiscalYear.IsClosed = false;
        fiscalYear = await DictionaryManager.SaveRecordAsync(fiscalYear);

        var fiscalPeriod = DictionaryManager.NewRecord<FiscalPeriod>();
        fiscalPeriod.Code = "P1";
        fiscalPeriod.FiscalYear = fiscalYear.MetaId;
        fiscalPeriod.FromDate = today.AddDays(-15);
        fiscalPeriod.ToDate = today.AddDays(15);
        fiscalPeriod.Status = "Open";
        await DictionaryManager.SaveRecordAsync(fiscalPeriod);

        await ConfigureTaxAsync();

        return new Setup
        {
            Cell = cell.MetaId,
            Item = item.MetaId,
            Supplier = supplier.MetaId,
            PayableAccount = payableAccount,
            VatAccount = vatAccount,
            InventoryAccount = inventoryAccount,
        };
    }

    private static async Task<Guid> NewAccountAsync(string code, string name, AccountType type, Guid currency)
    {
        var account = DictionaryManager.NewRecord<ChartOfAccounts>();
        account.Code = code;
        account.Name = name;
        account.AccountType = type;
        account.IsPostable = true;
        account.Currency = currency;
        return (await DictionaryManager.SaveRecordAsync(account)).MetaId;
    }

    private async Task ConfigureTaxAsync()
    {
        var from = new DateTime(2020, 1, 1);

        var authority = DictionaryManager.NewRecord<TaxAuthority>();
        authority.Code = $"ZAT-{Db.NewId():N}"[..10];
        authority.Name = "ZATCA";
        authority.CountryCode = "SA";
        authority.IsActive = true;
        authority = await DictionaryManager.SaveRecordAsync(authority);

        var jurisdiction = DictionaryManager.NewRecord<TaxJurisdiction>();
        jurisdiction.Code = $"SA-{Db.NewId():N}"[..10];
        jurisdiction.Name = "Saudi Arabia";
        jurisdiction.CountryCode = "SA";
        jurisdiction.Level = 0;
        jurisdiction = await DictionaryManager.SaveRecordAsync(jurisdiction);

        var tax = DictionaryManager.NewRecord<Tax>();
        tax.Code = $"VT-{Db.NewId():N}"[..10];
        tax.Name = "Saudi VAT";
        tax.Authority = authority.MetaId;
        tax.Jurisdiction = jurisdiction.MetaId;
        tax.EffectiveFrom = from;
        tax = await DictionaryManager.SaveRecordAsync(tax);

        var rate = DictionaryManager.NewRecord<TaxRate>();
        rate.Tax = tax.MetaId;
        rate.Code = $"R-{Db.NewId():N}"[..10];
        rate.Rate = 0.15m;
        rate.EffectiveFrom = from;
        rate = await DictionaryManager.SaveRecordAsync(rate);

        var category = DictionaryManager.NewRecord<TaxCategory>();
        category.Tax = tax.MetaId;
        category.Code = $"STD-{Db.NewId():N}"[..10];
        category.Treatment = "STANDARD";
        category = await DictionaryManager.SaveRecordAsync(category);

        var code = DictionaryManager.NewRecord<TaxCode>();
        code.Code = $"IN-{Db.NewId():N}"[..10];
        code.Name = "Standard 15%";
        code.Tax = tax.MetaId;
        code.TaxCategory = category.MetaId;
        code.TaxRate = rate.MetaId;
        code.EffectiveFrom = from;
        code = await DictionaryManager.SaveRecordAsync(code);

        var input = DictionaryManager.NewRecord<TaxDirection>();
        input.Code = "INPUT";
        input.Name = "Input";
        await DictionaryManager.SaveRecordAsync(input);

        var output = DictionaryManager.NewRecord<TaxDirection>();
        output.Code = "OUTPUT";
        output.Name = "Output";
        await DictionaryManager.SaveRecordAsync(output);

        var taxRows = await DictionaryManager.GetRecordsAsync<TaxSettings>(null, 1);
        var settings = taxRows.Count > 0 ? taxRows[0] : DictionaryManager.NewRecord<TaxSettings>();
        settings.DefaultTaxCode = code.Code;
        settings.PricesIncludeTax = false;
        await DictionaryManager.SaveRecordAsync(settings);
    }

    private async Task ReceiveAsync(Setup s, decimal quantity, decimal unitPrice)
    {
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Cell;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = quantity, UnitPrice = unitPrice });
        await DocumentManager.SaveDocumentAsync(order);

        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);

        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);
    }

    private static async Task<TaxCalculation> TheCalculationAsync()
    {
        var all = await DocumentManager.QueryDocumentsAsync<TaxCalculation>();
        Assert.IsTrue(all.Count == 1, "должен появиться один расчёт налога, факт {0}", all.Count);
        return (await DocumentManager.GetDocumentAsync<TaxCalculation>(all[0].MetaId))!;
    }

    private static async Task<(decimal Debit, decimal Credit)> AccountAsync(Guid document, Guid account)
    {
        decimal debit = 0m, credit = 0m;

        var family = await DocumentManager.GetDocumentFamilyAsync(document);
        var children = family.Edges.Where(e => e.ParentDocId == document).Select(e => e.ChildDocId).Distinct();

        foreach (var childId in children)
        {
            var entry = await DocumentManager.GetDocumentAsync<JournalEntry>(childId);
            if (entry == null) continue;
            foreach (var line in entry.Lines.Where(l => l.Account == account))
            {
                debit += line.Debit;
                credit += line.Credit;
            }
        }

        return (debit, credit);
    }

    [IntegrationTest("Входящий НДС дебетует счёт возмещения и доводит кредиторку до суммы с налогом")]
    public async Task InputVatBecomesAsset()
    {
        var s = await SetupAsync();

        // 10 × 3 = 30 базы, ставка 15% → налог 4.5.
        await ReceiveAsync(s, 10m, 3m);
        var calc = await TheCalculationAsync();

        var vat = await AccountAsync(calc.MetaId, s.VatAccount);
        Assert.IsTrue(vat.Debit == 4.5m,
            "налог 4.5 обязан стать активом, факт {0}", vat.Debit);

        var apFromTax = await AccountAsync(calc.MetaId, s.PayableAccount);
        Assert.IsTrue(apFromTax.Credit == 4.5m,
            "расчёт налога добирает недостающие 4.5 кредиторки, факт {0}", apFromTax.Credit);
    }

    [IntegrationTest("Без настроенного счёта возмещения приход проводится как прежде")]
    public async Task UnconfiguredVatAccountDoesNotBreakReceipt()
    {
        var s = await SetupAsync(configureVatAccount: false);

        await ReceiveAsync(s, 10m, 3m);
        var calc = await TheCalculationAsync();

        var vat = await AccountAsync(calc.MetaId, s.VatAccount);
        Assert.IsTrue(vat.Debit == 0m, "проводки по НДС нет, факт {0}", vat.Debit);
        Assert.IsTrue(calc.Lines[0].TaxAmount == 4.5m,
            "сам налог посчитан независимо от книги, факт {0}", calc.Lines[0].TaxAmount);
    }
}
