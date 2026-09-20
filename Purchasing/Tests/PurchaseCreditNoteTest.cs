using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

public class PurchaseCreditNoteTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Location;
        public Guid Item;
        public Guid ExtraItem;
        public Guid Supplier;
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
        legalEntity.RegistrationNumber = $"REG-PCN-{Db.NewId():N}"[..16];
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

        var extra = DictionaryManager.NewRecord<Item>();
        extra.Name = "Washer";
        extra.ItemGroup = group.MetaId;
        extra.UnitOfMeasure = uom.MetaId;
        extra.IsRawMaterial = true;
        extra = await DictionaryManager.SaveRecordAsync(extra);

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Bolt Supply Co";
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        if (configureTax)
            await ConfigureTaxAsync();
        else
            await ClearDefaultTaxAsync();

        return new Setup
        {
            Location = cell.MetaId,
            Item = item.MetaId,
            ExtraItem = extra.MetaId,
            Supplier = supplier.MetaId,
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
        code.Code = $"IN-{Db.NewId():N}"[..10];
        code.Name = "Standard 15%";
        code.Tax = tax.MetaId;
        code.TaxCategory = category.MetaId;
        code.TaxRate = rate.MetaId;
        code.EffectiveFrom = from;
        code = await DictionaryManager.SaveRecordAsync(code);

        if ((await DictionaryManager.GetRecordsAsync<TaxDirection>("Code = 'INPUT'", take: 1)).Count == 0)
        {
            var input = DictionaryManager.NewRecord<TaxDirection>();
            input.Code = "INPUT";
            input.Name = "Input";
            await DictionaryManager.SaveRecordAsync(input);
        }

        if ((await DictionaryManager.GetRecordsAsync<TaxDirection>("Code = 'OUTPUT'", take: 1)).Count == 0)
        {
            var output = DictionaryManager.NewRecord<TaxDirection>();
            output.Code = "OUTPUT";
            output.Name = "Output";
            await DictionaryManager.SaveRecordAsync(output);
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
        taxRows[0].DefaultTaxCode = null;
        await DictionaryManager.SaveRecordAsync(taxRows[0]);
    }

    private async Task<PurchaseOrder> ReceiveAsync(Setup s, decimal qty, decimal price)
    {
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);
        return (await DocumentManager.GetDocumentAsync<PurchaseOrder>(order.MetaId))!;
    }

    private Task<decimal> PayableAsync(Setup s)
        => TotalsManager.GetBalanceAsync("Payable", "Amount",
            new Dictionary<string, object?> { ["Supplier"] = s.Supplier });

    private Task<decimal> StockAsync(Setup s)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item });

    private static async Task<PurchaseCreditNote> NewNoteAsync(Setup s, Guid orderId)
    {
        var note = await DocumentManager.NewDocumentAsync<PurchaseCreditNote>();
        note.Supplier = s.Supplier;
        note.OriginalOrder = orderId;
        return note;
    }

    private async Task PostNoteAsync(PurchaseCreditNote note)
    {
        var commandId = await Db.FindCommandIdAsync("document", "PostPurchaseCreditNote");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, note.MetaId);
        Assert.IsTrue(run.Success, "команда PostPurchaseCreditNote: {0}",
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

    [IntegrationTest("Кредит-нота сторнирует НДС, заказ остаётся Received")]
    public async Task CreditReversesVatKeepsOrderReceived()
    {
        var s = await SetupAsync(configureTax: true);
        var order = await ReceiveAsync(s, 4m, 25m);
        Assert.IsTrue(await PayableAsync(s) == 115m, "долг 115, факт {0}", await PayableAsync(s));

        var note = await NewNoteAsync(s, order.MetaId);
        await DocumentManager.SaveDocumentAsync(note);
        var storedDraft = await DocumentManager.GetDocumentAsync<PurchaseCreditNote>(note.MetaId);
        Assert.IsTrue(storedDraft!.Lines.Count == 1, "пустые строки копируются с заказа, факт {0}", storedDraft.Lines.Count);
        Assert.IsTrue(storedDraft.TaxRateApplied == 0.15m,
            "ставка штампуется с даты заказа, факт {0}", storedDraft.TaxRateApplied);

        await PostNoteAsync(note);

        var posted = await DocumentManager.GetDocumentAsync<PurchaseCreditNote>(note.MetaId);
        Assert.IsTrue(posted!.Subtype == PurchaseCreditNote.Subtypes.Posted,
            "нота Posted, факт {0}", posted.Subtype);
        var storedOrder = await DocumentManager.GetDocumentAsync<PurchaseOrder>(order.MetaId);
        Assert.IsTrue(storedOrder!.Subtype == PurchaseOrder.Subtypes.Received,
            "заказ остаётся Received, факт {0}", storedOrder.Subtype);
        Assert.IsTrue(await PayableAsync(s) == 100m,
            "долг без НДС = 100, факт {0}", await PayableAsync(s));

        var calc = await LinkedCalcAsync(note.MetaId);
        Assert.IsNotNull(calc, "после проведения есть сторно TaxCalculation");
        Assert.IsTrue(calc!.Lines[0].TaxBase == -100m, "база сторно −100, факт {0}", calc.Lines[0].TaxBase);
        Assert.IsTrue(calc.Lines[0].TaxAmount == -15m, "налог сторно −15, факт {0}", calc.Lines[0].TaxAmount);
    }

    [IntegrationTest("Частичная кредит-нота сторнирует НДС только своих строк")]
    public async Task PartialCreditUsesOwnLines()
    {
        var s = await SetupAsync(configureTax: true);
        var order = await ReceiveAsync(s, 4m, 25m);

        var note = await NewNoteAsync(s, order.MetaId);
        note.Lines.Add(new PurchaseCreditNoteLinesTablePartRow
        {
            Item = s.Item,
            Quantity = 2m,
            UnitPrice = 25m,
        });
        await DocumentManager.SaveDocumentAsync(note);
        var stored = await DocumentManager.GetDocumentAsync<PurchaseCreditNote>(note.MetaId);
        Assert.IsTrue(stored!.Lines.Count == 1 && stored.Lines[0].Quantity == 2m,
            "свои строки не перезаписываются копией заказа");

        await PostNoteAsync(note);
        Assert.IsTrue(await PayableAsync(s) == 107.5m,
            "115 − 7.5 = 107.5, факт {0}", await PayableAsync(s));

        var calc = await LinkedCalcAsync(note.MetaId);
        Assert.IsTrue(calc!.Lines[0].TaxAmount == -7.5m,
            "частичное сторно −7.5, факт {0}", calc.Lines[0].TaxAmount);
    }

    [IntegrationTest("Строка сверх исходного заказа законна")]
    public async Task ExtraLineBeyondOrderAllowed()
    {
        var s = await SetupAsync(configureTax: true);
        var order = await ReceiveAsync(s, 4m, 25m);

        var note = await NewNoteAsync(s, order.MetaId);
        note.Lines.Add(new PurchaseCreditNoteLinesTablePartRow
        {
            Item = s.Item,
            Quantity = 4m,
            UnitPrice = 25m,
        });
        note.Lines.Add(new PurchaseCreditNoteLinesTablePartRow
        {
            Item = s.ExtraItem,
            Quantity = 1m,
            UnitPrice = 25m,
        });
        await DocumentManager.SaveDocumentAsync(note);
        await PostNoteAsync(note);

        Assert.IsTrue(await PayableAsync(s) == 96.25m,
            "115 − 18.75 = 96.25, факт {0}", await PayableAsync(s));
        var calc = await LinkedCalcAsync(note.MetaId);
        Assert.IsTrue(calc!.Lines[0].TaxBase == -125m, "база сверх заказа −125, факт {0}", calc.Lines[0].TaxBase);
    }

    [IntegrationTest("Кредит-нота только по оприходованному заказу")]
    public async Task RefusesUnorderedOrder()
    {
        var s = await SetupAsync(configureTax: true);
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Location;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Item, Quantity = 1m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(order);

        var note = await NewNoteAsync(s, order.MetaId);
        note.Lines.Add(new PurchaseCreditNoteLinesTablePartRow { Item = s.Item, Quantity = 1m, UnitPrice = 10m });
        note.TaxRateApplied = 0.15m;
        await DocumentManager.SaveDocumentAsync(note);

        var reason = "";
        try
        {
            note.Subtype = PurchaseCreditNote.Subtypes.Posted;
            await DocumentManager.SaveDocumentAsync(note);
        }
        catch (Exception ex)
        {
            for (Exception? e = ex; e is not null; e = e.InnerException) reason += e.Message + " | ";
        }
        Assert.IsTrue(reason.Contains("оприходованному"), "отказ про Received, факт: {0}", reason);
    }

    [IntegrationTest("Без НДС на приходе кредит-ноту проводить нечего")]
    public async Task RefusesWhenReceiptHasNoTax()
    {
        var s = await SetupAsync(configureTax: false);
        var order = await ReceiveAsync(s, 2m, 10m);

        var note = await NewNoteAsync(s, order.MetaId);
        note.Lines.Add(new PurchaseCreditNoteLinesTablePartRow { Item = s.Item, Quantity = 2m, UnitPrice = 10m });
        await DocumentManager.SaveDocumentAsync(note);

        var reason = "";
        try
        {
            note.Subtype = PurchaseCreditNote.Subtypes.Posted;
            await DocumentManager.SaveDocumentAsync(note);
        }
        catch (Exception ex)
        {
            for (Exception? e = ex; e is not null; e = e.InnerException) reason += e.Message + " | ";
        }
        Assert.IsTrue(reason.Contains("НДС") || reason.Contains("нечего"),
            "отказ про отсутствие НДС, факт: {0}", reason);
    }

    [IntegrationTest("Нота без возврата оставляет нетто кредиторки и склад")]
    public async Task NoteWithoutReturnLeavesNetPayable()
    {
        var s = await SetupAsync(configureTax: true);
        var order = await ReceiveAsync(s, 4m, 25m);
        Assert.IsTrue(await StockAsync(s) == 4m, "склад 4 после прихода");

        var note = await NewNoteAsync(s, order.MetaId);
        await DocumentManager.SaveDocumentAsync(note);
        await PostNoteAsync(note);

        Assert.IsTrue(await PayableAsync(s) == 100m, "нетто 100, факт {0}", await PayableAsync(s));
        Assert.IsTrue(await StockAsync(s) == 4m, "склад нота не трогает, факт {0}", await StockAsync(s));
        var storedOrder = await DocumentManager.GetDocumentAsync<PurchaseOrder>(order.MetaId);
        Assert.IsTrue(storedOrder!.Subtype == PurchaseOrder.Subtypes.Received, "заказ не Voided");
    }

    [IntegrationTest("Возврат снимает нетто, кредит-нота — НДС, долг в ноль")]
    public async Task ReturnThenCreditClearsGross()
    {
        var s = await SetupAsync(configureTax: true);
        var order = await ReceiveAsync(s, 4m, 25m);
        Assert.IsTrue(await PayableAsync(s) == 115m, "долг 115");

        var ret = await DocumentManager.NewDocumentAsync<PurchaseReturn>();
        ret.OriginalOrder = order.MetaId;
        await DocumentManager.SaveDocumentAsync(ret);
        await RunCommandAsync("PostPurchaseReturn", ret.MetaId);
        Assert.IsTrue(await PayableAsync(s) == 15m,
            "возврат снимает нетто, остаётся НДС 15, факт {0}", await PayableAsync(s));

        var note = await NewNoteAsync(s, order.MetaId);
        await DocumentManager.SaveDocumentAsync(note);
        await PostNoteAsync(note);
        Assert.IsTrue(await PayableAsync(s) == 0m,
            "нота гасит оставшийся НДС, факт {0}", await PayableAsync(s));
        var storedOrder = await DocumentManager.GetDocumentAsync<PurchaseOrder>(order.MetaId);
        Assert.IsTrue(storedOrder!.Subtype == PurchaseOrder.Subtypes.Received, "заказ не Voided");
    }

    private async Task RunCommandAsync(string name, Guid documentId)
    {
        var commandId = await Db.FindCommandIdAsync("document", name);
        var run = await Db.ExecuteDocumentCommandAsync(commandId, documentId);
        Assert.IsTrue(run.Success, "команда {0}: {1}", name, run.Message ?? string.Join("; ", run.ClientMessages));
    }
}
