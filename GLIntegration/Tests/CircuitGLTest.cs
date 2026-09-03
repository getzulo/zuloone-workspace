using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Три книги на одной строке GL: выручка (FIN,MGT) не пишет налог, НДС (FIN,TAX)
// не пишет управленческую. Проверка идёт по движениям JournalEntry, а не по
// сумме всей книги — иначе схлопывание ресурсов спрятало бы ошибку контура.
public class CircuitGLTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Cell;
        public Guid Item;
        public Guid Customer;
    }

    private async Task<Setup> SetupAsync()
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
        legalEntity.RegistrationNumber = $"REG-CIR-{Db.NewId():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"SP-{Db.NewId():N}"[..12];
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
        zone.Name = "Зона";
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

        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = $"PCS-{Db.NewId():N}"[..12];
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"GOODS-{Db.NewId():N}"[..12];
        group.Name = "Finished goods";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = "Gadget";
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = uom.MetaId;
        item.IsSellable = true;
        item = await DictionaryManager.SaveRecordAsync(item);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        await TotalsManager.PostMovementAsync("Stock", null, today,
            new Dictionary<string, object?> { ["Cell"] = cell.MetaId, ["Item"] = item.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 100m });

        await NewAccountAsync("1200", "Accounts receivable", AccountType.Asset, currency.MetaId);
        await NewAccountAsync("2300", "VAT payable", AccountType.Liability, currency.MetaId);
        await NewAccountAsync("4000", "Revenue", AccountType.Income, currency.MetaId);
        await NewAccountAsync("1000", "Cash", AccountType.Asset, currency.MetaId);

        var accRows = await DictionaryManager.GetRecordsAsync<AccountingSettings>(null, 1);
        var settings = accRows.Count > 0 ? accRows[0] : DictionaryManager.NewRecord<AccountingSettings>();
        settings.ArAccountCode = "1200";
        settings.RevenueAccountCode = "4000";
        settings.VatPayableAccountCode = "2300";
        settings.CashAccountCode = "1000";
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
            Customer = customer.MetaId,
        };
    }

    private static async Task NewAccountAsync(string code, string name, AccountType type, Guid currency)
    {
        var account = DictionaryManager.NewRecord<ChartOfAccounts>();
        account.Code = code;
        account.Name = name;
        account.AccountType = type;
        account.IsPostable = true;
        account.Currency = currency;
        await DictionaryManager.SaveRecordAsync(account);
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
        code.Code = $"OUT-{Db.NewId():N}"[..10];
        code.Name = "Standard 15%";
        code.Tax = tax.MetaId;
        code.TaxCategory = category.MetaId;
        code.TaxRate = rate.MetaId;
        code.EffectiveFrom = from;
        code = await DictionaryManager.SaveRecordAsync(code);

        var direction = DictionaryManager.NewRecord<TaxDirection>();
        direction.Code = "OUTPUT";
        direction.Name = "Output";
        await DictionaryManager.SaveRecordAsync(direction);

        var taxRows = await DictionaryManager.GetRecordsAsync<TaxSettings>(null, 1);
        var settings = taxRows.Count > 0 ? taxRows[0] : DictionaryManager.NewRecord<TaxSettings>();
        settings.DefaultTaxCode = code.Code;
        settings.PricesIncludeTax = false;
        await DictionaryManager.SaveRecordAsync(settings);
    }

    private static async Task<SalesInvoice> IssueAsync(Setup s, decimal quantity, decimal unitPrice)
    {
        var invoice = await DocumentManager.NewDocumentAsync<SalesInvoice>();
        invoice.Customer = s.Customer;
        invoice.Location = s.Cell;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = quantity, UnitPrice = unitPrice });
        await DocumentManager.SaveDocumentAsync(invoice);

        invoice.Subtype = SalesInvoice.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);
        return invoice;
    }

    private static async Task<JournalEntry?> FindJournalAsync(Guid parent, string descriptionPrefix)
    {
        var family = await DocumentManager.GetDocumentFamilyAsync(parent);
        foreach (var childId in family.Edges.Where(e => e.ParentDocId == parent).Select(e => e.ChildDocId).Distinct())
        {
            var entry = await DocumentManager.GetDocumentAsync<JournalEntry>(childId);
            if (entry != null && (entry.Description ?? "").StartsWith(descriptionPrefix, StringComparison.Ordinal))
                return entry;
        }
        return null;
    }

    private static async Task<(decimal Debit, decimal Credit, decimal ManagementDebit, decimal ManagementCredit, decimal TaxDebit, decimal TaxCredit)>
        BooksAsync(Guid journalEntry)
    {
        decimal debit = 0m, credit = 0m, mgtDr = 0m, mgtCr = 0m, taxDr = 0m, taxCr = 0m;
        foreach (var row in await TotalsManager.QueryMovementsAsync("GL", $"[DocumentMetaId] = '{journalEntry}'"))
        {
            debit += Convert.ToDecimal(row["Debit"]);
            credit += Convert.ToDecimal(row["Credit"]);
            mgtDr += Convert.ToDecimal(row["ManagementDebit"]);
            mgtCr += Convert.ToDecimal(row["ManagementCredit"]);
            taxDr += Convert.ToDecimal(row["TaxDebit"]);
            taxCr += Convert.ToDecimal(row["TaxCredit"]);
        }
        return (debit, credit, mgtDr, mgtCr, taxDr, taxCr);
    }

    [IntegrationTest("Выручка заполняет финансовую и управленческую книги, налоговую — нулями")]
    public async Task RevenueJournalFillsManagementNotTax()
    {
        var s = await SetupAsync();
        var invoice = await IssueAsync(s, 4m, 25m);

        var revenue = await FindJournalAsync(invoice.MetaId, "Sales invoice");
        Assert.IsTrue(revenue != null, "счёт обязан породить проводку выручки");

        var books = await BooksAsync(revenue!.MetaId);
        Assert.IsTrue(books.Debit == 100m && books.Credit == 100m,
            "финансовая книга 100/100, факт {0}/{1}", books.Debit, books.Credit);
        Assert.IsTrue(books.ManagementDebit == 100m && books.ManagementCredit == 100m,
            "управленческая книга равна финансовой, факт {0}/{1}",
            books.ManagementDebit, books.ManagementCredit);
        Assert.IsTrue(books.TaxDebit == 0m && books.TaxCredit == 0m,
            "налоговая книга на выручке пуста, факт {0}/{1}", books.TaxDebit, books.TaxCredit);
    }

    [IntegrationTest("НДС заполняет финансовую и налоговую книги, управленческую — нулями")]
    public async Task VatJournalFillsTaxNotManagement()
    {
        var s = await SetupAsync();
        await IssueAsync(s, 4m, 25m);

        var all = await DocumentManager.QueryDocumentsAsync<TaxCalculation>();
        Assert.IsTrue(all.Count == 1, "должен появиться один расчёт налога, факт {0}", all.Count);

        var vat = await FindJournalAsync(all[0].MetaId, "Output VAT");
        Assert.IsTrue(vat != null, "расчёт налога обязан породить проводку НДС");

        var books = await BooksAsync(vat!.MetaId);
        Assert.IsTrue(books.Debit == 15m && books.Credit == 15m,
            "финансовая книга НДС 15/15, факт {0}/{1}", books.Debit, books.Credit);
        Assert.IsTrue(books.TaxDebit == 15m && books.TaxCredit == 15m,
            "налоговая книга равна НДС, факт {0}/{1}", books.TaxDebit, books.TaxCredit);
        Assert.IsTrue(books.ManagementDebit == 0m && books.ManagementCredit == 0m,
            "управленческая книга на НДС пуста, факт {0}/{1}",
            books.ManagementDebit, books.ManagementCredit);
    }
}
