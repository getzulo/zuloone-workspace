#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Data;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Same paper as CreditNoteXr, for additional VAT. Buyer release is the
// country-blind IEInvoiceRelease door — Sales does not name a localization.
public partial class DebitNoteXrPrintForm : PrintFormBase
{
    public override SlimTable GetDataTemplate()
        => new SlimTable(Row("", "", "", "", "", "", "", "", "", 0m, 0m, 0m, 0, "", 0m, "", 0m, 0m, 0m, 0m, "", ""));

    public override async Task<SlimTable> GetDataAsync(PrintFormContext context)
    {
        var table = new SlimTable("Report");
        var documents = context.GetService<IDocumentManager>();
        var doc = await documents.GetDocumentAsync<SalesDebitNote>(context.RecordId);
        if (doc == null)
        {
            table.Add(Row("", "", "", "", "", "", "", "", "", 0m, 0m, 0m, 0, "", 0m, "", 0m, 0m, 0m, 0m, "", ""));
            return table;
        }

        var block = await context.GetService<IEInvoiceRelease>()
            .BuyerReleaseBlockAsync(doc.MetaId);
        if (!string.IsNullOrEmpty(block))
            throw new InvalidOperationException(block);

        var display = context.GetService<IReferenceDisplay>();
        var pricing = context.GetService<IPricingService>();
        var taxes = context.GetService<ITaxService>();
        var ct = context.CancellationToken;
        var sellerName = await NameAsync(display, "LegalEntity", doc.LegalEntity, ct);
        var customerName = await NameAsync(display, "Customer", doc.Customer, ct);
        var contractName = await NameAsync(display, "SalesContract", doc.Contract, ct);
        var outletName = await NameAsync(display, "CustomerOutlet", doc.Outlet, ct);
        var original = await NameAsync(display, "SalesRealization", doc.OriginalInvoice, ct);

        decimal net = 0m;
        foreach (var line in doc.Lines)
            net += pricing.LineAmount(line.Quantity, line.UnitPrice);
        var taxTotal = doc.TaxRateApplied > 0m ? taxes.CalculateTax(net, doc.TaxRateApplied) : 0m;
        var subTotal = net;
        var total = net + taxTotal;

        var n = 0;
        foreach (var line in doc.Lines)
        {
            n++;
            var itemText = await NameAsync(display, "Item", line.Item, ct);
            table.Add(Row(
                doc.ID ?? "",
                DateText(doc.DocumentDate),
                customerName,
                "",
                sellerName,
                contractName,
                outletName,
                "",
                original,
                subTotal,
                taxTotal,
                total,
                n,
                itemText,
                line.Quantity,
                "",
                line.UnitPrice,
                pricing.LineAmount(line.Quantity, line.UnitPrice),
                0m,
                0m,
                "",
                ""));
        }

        if (n == 0)
        {
            table.Add(Row(
                doc.ID ?? "",
                DateText(doc.DocumentDate),
                customerName,
                "",
                sellerName,
                contractName,
                outletName,
                "",
                original,
                subTotal,
                taxTotal,
                total,
                0,
                "",
                0m,
                "",
                0m,
                0m,
                0m,
                0m,
                "",
                ""));
        }

        return table;
    }

    private static object Row(
        string number,
        string documentDate,
        string customer,
        string supplier,
        string seller,
        string contract,
        string outlet,
        string location,
        string notes,
        decimal subTotal,
        decimal taxAmount,
        decimal total,
        int lineNo,
        string item,
        decimal quantity,
        string unit,
        decimal unitPrice,
        decimal amount,
        decimal debit,
        decimal credit,
        string lineParty,
        string lineNote)
        => new
        {
            Number = number,
            DocumentDate = documentDate,
            Customer = customer,
            Supplier = supplier,
            Seller = seller,
            Contract = contract,
            Outlet = outlet,
            Location = location,
            Notes = notes,
            SubTotal = subTotal,
            TaxAmount = taxAmount,
            Total = total,
            LineNo = lineNo,
            Item = item,
            Quantity = quantity,
            Unit = unit,
            UnitPrice = unitPrice,
            Amount = amount,
            Debit = debit,
            Credit = credit,
            LineParty = lineParty,
            LineNote = lineNote,
        };

    private static async Task<string> NameAsync(
        IReferenceDisplay display, string dictionary, Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) return "";
        return await display.FormatAsync(dictionary, id, ct) ?? "";
    }

    private static string DateText(DateTime value)
        => value.Year >= 1902 ? value.ToString("yyyy-MM-dd") : "";
}
