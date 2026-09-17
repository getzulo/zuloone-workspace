using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

public class SalesCreditNoteTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Cell;
        public Guid Item;
        public Guid ExtraItem;
        public Guid Customer;
        public Guid Outlet;
        public Guid Contract;
        public Guid LegalEntity;
    }

    private async Task<Setup> SetupAsync(bool configureTax)
    {
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
        legalEntity.RegistrationNumber = $"REG-CN-{Db.NewId():N}"[..16];
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

        var extra = DictionaryManager.NewRecord<Item>();
        extra.Name = "Addon";
        extra.ItemGroup = group.MetaId;
        extra.UnitOfMeasure = uom.MetaId;
        extra.IsSellable = true;
        extra = await DictionaryManager.SaveRecordAsync(extra);

        var customer = DictionaryManager.NewRecord<Customer>();
        customer.Name = "Buyer Ltd";
        customer.CustomerType = "B2B";
        customer = await DictionaryManager.SaveRecordAsync(customer);

        var outlet = DictionaryManager.NewRecord<CustomerOutlet>();
        outlet.Name = "Shop A";
        outlet.Customer = customer.MetaId;
        outlet = await DictionaryManager.SaveRecordAsync(outlet);

        var contract = DictionaryManager.NewRecord<SalesContract>();
        contract.Name = "A-2026";
        contract.Outlet = outlet.MetaId;
        contract.Currency = currency.MetaId;
        contract.SettlementKind = SettlementKind.Credit;
        contract.EffectiveFrom = new DateTime(2020, 1, 1);
        contract = await DictionaryManager.SaveRecordAsync(contract);

        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = cell.MetaId, ["Item"] = item.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 100m });

        if (configureTax)
            await ConfigureTaxAsync();
        else
            await ClearDefaultTaxAsync();

        return new Setup
        {
            Cell = cell.MetaId,
            Item = item.MetaId,
            ExtraItem = extra.MetaId,
            Customer = customer.MetaId,
            Outlet = outlet.MetaId,
            Contract = contract.MetaId,
            LegalEntity = legalEntity.MetaId,
        };
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

        if ((await DictionaryManager.GetRecordsAsync<TaxDirection>("Code = 'OUTPUT'", take: 1)).Count == 0)
        {
            var direction = DictionaryManager.NewRecord<TaxDirection>();
            direction.Code = "OUTPUT";
            direction.Name = "Output";
            await DictionaryManager.SaveRecordAsync(direction);
        }

        var taxRows = await DictionaryManager.GetRecordsAsync<TaxSettings>(null, 1);
        var settings = taxRows.Count > 0 ? taxRows[0] : DictionaryManager.NewRecord<TaxSettings>();
        settings.DefaultTaxCode = code.Code;
        settings.PricesIncludeTax = false;
        await DictionaryManager.SaveRecordAsync(settings);
    }

    private async Task ClearDefaultTaxAsync()
    {
        var taxRows = await DictionaryManager.GetRecordsAsync<TaxSettings>(null, 1);
        if (taxRows.Count == 0) return;
        taxRows[0].DefaultTaxCode = "";
        await DictionaryManager.SaveRecordAsync(taxRows[0]);
    }

    private static async Task<SalesRealization> IssueAsync(Setup s, decimal quantity, decimal unitPrice)
    {
        var invoice = await DocumentManager.NewDocumentAsync<SalesRealization>();
        invoice.Customer = s.Customer;
        invoice.Outlet = s.Outlet;
        invoice.Contract = s.Contract;
        invoice.Location = s.Cell;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow
        {
            Item = s.Item,
            Quantity = quantity,
            UnitPrice = unitPrice,
        });
        await DocumentManager.SaveDocumentAsync(invoice);
        invoice.Subtype = SalesRealization.Subtypes.Issued;
        await DocumentManager.SaveDocumentAsync(invoice);
        return (await DocumentManager.GetDocumentAsync<SalesRealization>(invoice.MetaId))!;
    }

    private static Task<decimal> ReceivableAsync(Setup s)
        => TotalsManager.GetBalanceAsync("Receivable", "Amount",
            new Dictionary<string, object?> { ["Customer"] = s.Customer, ["SalesContract"] = s.Contract });

    private static Task<decimal> VatPayableAsync(Setup s)
        => TotalsManager.GetBalanceAsync("VatPayable", "Amount",
            new Dictionary<string, object?> { ["Customer"] = s.Customer, ["SalesContract"] = s.Contract });

    private static async Task<SalesCreditNote> NewNoteAsync(Setup s, Guid invoiceId)
    {
        var note = await DocumentManager.NewDocumentAsync<SalesCreditNote>();
        note.Customer = s.Customer;
        note.Outlet = s.Outlet;
        note.Contract = s.Contract;
        note.OriginalInvoice = invoiceId;
        return note;
    }

    private async Task PostNoteAsync(SalesCreditNote note)
    {
        var commandId = await Db.FindCommandIdAsync("document", "PostSalesCreditNote");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, note.MetaId);
        Assert.IsTrue(run.Success, "команда PostSalesCreditNote: {0}",
            run.Message ?? string.Join("; ", run.ClientMessages));
    }

    private static async Task<TaxCalculation?> LinkedCalcAsync(Guid parentId)
    {
        var family = await DocumentManager.GetDocumentFamilyAsync(parentId);
        foreach (var id in family.Edges.Where(e => e.ParentDocId == parentId).Select(e => e.ChildDocId).Distinct())
        {
            var calc = await DocumentManager.GetDocumentAsync<TaxCalculation>(id);
            if (calc != null) return calc;
        }
        return null;
    }

    [IntegrationTest("Кредит-нота сторнирует НДС, счёт остаётся Issued")]
    public async Task CreditReversesVatKeepsInvoiceIssued()
    {
        var s = await SetupAsync(configureTax: true);
        var inv = await IssueAsync(s, 4m, 25m);
        Assert.IsTrue(await ReceivableAsync(s) == 115m, "долг 115, факт {0}", await ReceivableAsync(s));

        var note = await NewNoteAsync(s, inv.MetaId);
        await DocumentManager.SaveDocumentAsync(note);
        var storedDraft = await DocumentManager.GetDocumentAsync<SalesCreditNote>(note.MetaId);
        Assert.IsTrue(storedDraft!.Lines.Count == 1, "пустые строки копируются со счёта, факт {0}", storedDraft.Lines.Count);
        Assert.IsTrue(storedDraft.TaxRateApplied == inv.TaxRateApplied,
            "ставка скопирована со счёта, факт {0}", storedDraft.TaxRateApplied);

        await PostNoteAsync(note);

        var posted = await DocumentManager.GetDocumentAsync<SalesCreditNote>(note.MetaId);
        Assert.IsTrue(posted!.Subtype == SalesCreditNote.Subtypes.Posted,
            "нота Posted, факт {0}", posted.Subtype);
        var invoice = await DocumentManager.GetDocumentAsync<SalesRealization>(inv.MetaId);
        Assert.IsTrue(invoice!.Subtype == SalesRealization.Subtypes.Issued,
            "счёт остаётся Issued, факт {0}", invoice.Subtype);
        Assert.IsTrue(await ReceivableAsync(s) == 100m,
            "долг без НДС = 100, факт {0}", await ReceivableAsync(s));
        Assert.IsTrue(await VatPayableAsync(s) == 0m,
            "VatPayable неттится, факт {0}", await VatPayableAsync(s));

        var calc = await LinkedCalcAsync(note.MetaId);
        Assert.IsNotNull(calc, "после проведения есть сторно TaxCalculation");
        Assert.IsTrue(calc!.Lines[0].TaxBase == -100m, "база сторно −100, факт {0}", calc.Lines[0].TaxBase);
        Assert.IsTrue(calc.Lines[0].TaxAmount == -15m, "налог сторно −15, факт {0}", calc.Lines[0].TaxAmount);
    }

    [IntegrationTest("Частичная кредит-нота сторнирует НДС только своих строк")]
    public async Task PartialCreditUsesOwnLines()
    {
        var s = await SetupAsync(configureTax: true);
        var inv = await IssueAsync(s, 4m, 25m);

        var note = await NewNoteAsync(s, inv.MetaId);
        note.Lines.Add(new SalesCreditNoteLinesTablePartRow
        {
            Item = s.Item,
            Quantity = 2m,
            UnitPrice = 25m,
        });
        await DocumentManager.SaveDocumentAsync(note);
        var stored = await DocumentManager.GetDocumentAsync<SalesCreditNote>(note.MetaId);
        Assert.IsTrue(stored!.Lines.Count == 1 && stored.Lines[0].Quantity == 2m,
            "свои строки не перезаписываются копией счёта");

        await PostNoteAsync(note);
        Assert.IsTrue(await ReceivableAsync(s) == 107.5m,
            "115 − 7.5 = 107.5, факт {0}", await ReceivableAsync(s));

        var calc = await LinkedCalcAsync(note.MetaId);
        Assert.IsTrue(calc!.Lines[0].TaxAmount == -7.5m,
            "частичное сторно −7.5, факт {0}", calc.Lines[0].TaxAmount);
    }

    [IntegrationTest("Строка сверх исходного счёта законна")]
    public async Task ExtraLineBeyondInvoiceAllowed()
    {
        var s = await SetupAsync(configureTax: true);
        var inv = await IssueAsync(s, 4m, 25m);

        var note = await NewNoteAsync(s, inv.MetaId);
        note.Lines.Add(new SalesCreditNoteLinesTablePartRow
        {
            Item = s.Item,
            Quantity = 4m,
            UnitPrice = 25m,
        });
        note.Lines.Add(new SalesCreditNoteLinesTablePartRow
        {
            Item = s.ExtraItem,
            Quantity = 1m,
            UnitPrice = 25m,
        });
        await DocumentManager.SaveDocumentAsync(note);
        await PostNoteAsync(note);

        Assert.IsTrue(await ReceivableAsync(s) == 96.25m,
            "115 − 18.75 = 96.25, факт {0}", await ReceivableAsync(s));
        var calc = await LinkedCalcAsync(note.MetaId);
        Assert.IsTrue(calc!.Lines[0].TaxBase == -125m, "база сверх счёта −125, факт {0}", calc.Lines[0].TaxBase);
    }

    [IntegrationTest("Кредит-нота только по реализованному счёту")]
    public async Task RefusesDraftInvoice()
    {
        var s = await SetupAsync(configureTax: true);
        var invoice = await DocumentManager.NewDocumentAsync<SalesRealization>();
        invoice.Customer = s.Customer;
        invoice.Outlet = s.Outlet;
        invoice.Contract = s.Contract;
        invoice.Location = s.Cell;
        invoice.Lines.Add(new SalesInvoiceLinesTablePartRow { Item = s.Item, Quantity = 1m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(invoice);

        var note = await NewNoteAsync(s, invoice.MetaId);
        note.Lines.Add(new SalesCreditNoteLinesTablePartRow { Item = s.Item, Quantity = 1m, UnitPrice = 10m });
        note.TaxRateApplied = 0.15m;
        await DocumentManager.SaveDocumentAsync(note);

        var reason = "";
        try
        {
            note.Subtype = SalesCreditNote.Subtypes.Posted;
            await DocumentManager.SaveDocumentAsync(note);
        }
        catch (Exception ex)
        {
            for (Exception? e = ex; e is not null; e = e.InnerException) reason += e.Message + " | ";
        }
        Assert.IsTrue(reason.Contains("реализованному"), "отказ про Issued, факт: {0}", reason);
    }

    [IntegrationTest("Без НДС на счёте кредит-ноту проводить нечего")]
    public async Task RefusesWhenInvoiceHasNoTax()
    {
        var s = await SetupAsync(configureTax: false);
        var inv = await IssueAsync(s, 2m, 10m);
        Assert.IsTrue(inv.TaxRateApplied == 0m, "ставка не штампуется без контура");

        var note = await NewNoteAsync(s, inv.MetaId);
        note.Lines.Add(new SalesCreditNoteLinesTablePartRow { Item = s.Item, Quantity = 2m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(note);

        var reason = "";
        try
        {
            note.Subtype = SalesCreditNote.Subtypes.Posted;
            await DocumentManager.SaveDocumentAsync(note);
        }
        catch (Exception ex)
        {
            for (Exception? e = ex; e is not null; e = e.InnerException) reason += e.Message + " | ";
        }
        Assert.IsTrue(reason.Contains("НДС"), "отказ про отсутствие НДС, факт: {0}", reason);
    }

    [IntegrationTest("Возврат снимает нетто, кредит-нота — НДС, долг в ноль")]
    public async Task ReturnThenCreditClearsGross()
    {
        var s = await SetupAsync(configureTax: true);
        var inv = await IssueAsync(s, 4m, 25m);
        Assert.IsTrue(await ReceivableAsync(s) == 115m, "долг 115");

        var ret = await DocumentManager.NewDocumentAsync<SalesReturn>();
        ret.Customer = s.Customer;
        ret.Outlet = s.Outlet;
        ret.Contract = s.Contract;
        ret.Location = s.Cell;
        ret.OriginalInvoice = inv.MetaId;
        ret.Lines.Add(new SalesReturnLinesTablePartRow { Item = s.Item, Quantity = 4m, UnitPrice = 25m });
        await DocumentManager.SaveDocumentAsync(ret);
        ret.Subtype = SalesReturn.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(ret);
        Assert.IsTrue(await ReceivableAsync(s) == 15m,
            "возврат снимает нетто, остаётся НДС 15, факт {0}", await ReceivableAsync(s));

        var note = await NewNoteAsync(s, inv.MetaId);
        await DocumentManager.SaveDocumentAsync(note);
        await PostNoteAsync(note);
        Assert.IsTrue(await ReceivableAsync(s) == 0m,
            "нота гасит оставшийся НДС, факт {0}", await ReceivableAsync(s));
        var invoice = await DocumentManager.GetDocumentAsync<SalesRealization>(inv.MetaId);
        Assert.IsTrue(invoice!.Subtype == SalesRealization.Subtypes.Issued, "счёт не Voided");
    }
}
