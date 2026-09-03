using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Шов Receivable ↔ GL НДС: регистр вёлся без налога, книга — с налогом.
// Оплата гросса гасила регистр в −налог. TaxCalculationGL дописывает налог
// в Receivable, чтобы платёж 115 закрыл и регистр, и счёт в книге.
public class ReceivableTaxSeamTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Cell;
        public Guid Item;
        public Guid Customer;
        public Guid Supplier;
        public Guid LegalEntity;
    }

    private async Task<Setup> SetupAsync(bool configureTax)
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
        legalEntity.RegistrationNumber = $"REG-SEAM-{Db.NewId():N}"[..16];
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

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Bolt Supply Co";
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        await TotalsManager.PostMovementAsync("Stock", null, today,
            new Dictionary<string, object?> { ["Cell"] = cell.MetaId, ["Item"] = item.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 100m });

        await NewAccountAsync("1200", "Accounts receivable", AccountType.Asset, currency.MetaId);
        await NewAccountAsync("1300", "Inventory", AccountType.Asset, currency.MetaId);
        await NewAccountAsync("2100", "Accounts payable", AccountType.Liability, currency.MetaId);
        await NewAccountAsync("2300", "VAT payable", AccountType.Liability, currency.MetaId);
        await NewAccountAsync("2400", "VAT receivable", AccountType.Asset, currency.MetaId);
        await NewAccountAsync("4000", "Revenue", AccountType.Income, currency.MetaId);
        await NewAccountAsync("1000", "Cash", AccountType.Asset, currency.MetaId);

        var accRows = await DictionaryManager.GetRecordsAsync<AccountingSettings>(null, 1);
        var settings = accRows.Count > 0 ? accRows[0] : DictionaryManager.NewRecord<AccountingSettings>();
        settings.ArAccountCode = "1200";
        settings.InventoryAccountCode = "1300";
        settings.PayableAccountCode = "2100";
        settings.RevenueAccountCode = "4000";
        settings.VatPayableAccountCode = "2300";
        settings.VatReceivableAccountCode = "2400";
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

        if (configureTax)
            await ConfigureTaxAsync();
        else
            await ClearDefaultTaxAsync();

        return new Setup
        {
            Cell = cell.MetaId,
            Item = item.MetaId,
            Customer = customer.MetaId,
            Supplier = supplier.MetaId,
            LegalEntity = legalEntity.MetaId,
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

    private static async Task ClearDefaultTaxAsync()
    {
        var taxRows = await DictionaryManager.GetRecordsAsync<TaxSettings>(null, 1);
        if (taxRows.Count == 0) return;
        taxRows[0].DefaultTaxCode = null;
        await DictionaryManager.SaveRecordAsync(taxRows[0]);
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

        var output = DictionaryManager.NewRecord<TaxDirection>();
        output.Code = "OUTPUT";
        output.Name = "Output";
        await DictionaryManager.SaveRecordAsync(output);

        var input = DictionaryManager.NewRecord<TaxDirection>();
        input.Code = "INPUT";
        input.Name = "Input";
        await DictionaryManager.SaveRecordAsync(input);

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

    private static async Task<decimal> ReceivableAsync()
    {
        decimal total = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("Receivable"))
            total += Convert.ToDecimal(r["Amount"]);
        return total;
    }

    [IntegrationTest("С налогом дебиторка 115, оплата 115 гасит регистр в ноль")]
    public async Task TaxInclusiveReceivableClearedByGrossPayment()
    {
        var s = await SetupAsync(configureTax: true);

        await IssueAsync(s, 4m, 25m);
        Assert.IsTrue(await ReceivableAsync() == 115m,
            "долг 100 + налог 15 = 115, факт {0}", await ReceivableAsync());

        var payment = await DocumentManager.NewDocumentAsync<CustomerPayment>();
        payment.LegalEntity = s.LegalEntity;
        payment.Lines.Add(new CustomerPaymentLinesTablePartRow { Customer = s.Customer, Amount = 115m });
        await DocumentManager.SaveDocumentAsync(payment);

        payment.Subtype = CustomerPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(payment);

        Assert.IsTrue(await ReceivableAsync() == 0m,
            "оплата 115 гасит регистр, факт {0}", await ReceivableAsync());
    }

    [IntegrationTest("Без налоговой настройки дебиторка остаётся 100")]
    public async Task InvoiceWithoutTaxKeepsNetReceivable()
    {
        var s = await SetupAsync(configureTax: false);

        await IssueAsync(s, 4m, 25m);
        Assert.IsTrue(await ReceivableAsync() == 100m,
            "без налога долг 100, лишнего движения нет, факт {0}", await ReceivableAsync());
    }

    private static async Task ReceiveAsync(Setup s, decimal quantity, decimal unitPrice)
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

    private static async Task<decimal> PayableAsync()
    {
        decimal total = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("Payable"))
            total += Convert.ToDecimal(r["Amount"]);
        return total;
    }

    [IntegrationTest("С налогом кредиторка 115, оплата 115 гасит регистр в ноль")]
    public async Task TaxInclusivePayableClearedByGrossPayment()
    {
        var s = await SetupAsync(configureTax: true);

        await ReceiveAsync(s, 4m, 25m);
        Assert.IsTrue(await PayableAsync() == 115m,
            "долг 100 + входной налог 15 = 115, факт {0}", await PayableAsync());

        var payment = await DocumentManager.NewDocumentAsync<VendorPayment>();
        payment.LegalEntity = s.LegalEntity;
        payment.Lines.Add(new VendorPaymentLinesTablePartRow { Supplier = s.Supplier, Amount = 115m });
        await DocumentManager.SaveDocumentAsync(payment);

        payment.Subtype = VendorPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(payment);

        Assert.IsTrue(await PayableAsync() == 0m,
            "оплата 115 гасит кредиторку, факт {0}", await PayableAsync());
    }

    [IntegrationTest("Без налоговой настройки кредиторка остаётся 100")]
    public async Task ReceiptWithoutTaxKeepsNetPayable()
    {
        var s = await SetupAsync(configureTax: false);

        await ReceiveAsync(s, 4m, 25m);
        Assert.IsTrue(await PayableAsync() == 100m,
            "без налога долг 100, лишнего движения нет, факт {0}", await PayableAsync());
    }
}
