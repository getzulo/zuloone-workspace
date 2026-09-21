#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Data;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Generated from the print-form spec table, then owned by hand.
// One uniform Row(...) feeds GetDataTemplate and GetDataAsync so the template
// and the data can never disagree — the failure InvoiceXr shipped with.
public partial class PurchaseOrderXrPrintForm : PrintFormBase
{
    public override SlimTable GetDataTemplate()
        => new SlimTable(Row("", "", "", "", "", "", "", "", "", 0m, 0m, 0m, 0, "", 0m, "", 0m, 0m, 0m, 0m, "", ""));

    public override async Task<SlimTable> GetDataAsync(PrintFormContext context)
    {
        var table = new SlimTable("Report");
        var documents = context.GetService<IDocumentManager>();
        var doc = await documents.GetDocumentAsync<PurchaseOrder>(context.RecordId);
        if (doc == null)
        {
            table.Add(Row("", "", "", "", "", "", "", "", "", 0m, 0m, 0m, 0, "", 0m, "", 0m, 0m, 0m, 0m, "", ""));
            return table;
        }

        var display = context.GetService<IReferenceDisplay>();
        var pricing = context.GetService<IPricingService>();
        var ct = context.CancellationToken;
        var supplierName = await NameAsync(display, "Supplier", doc.Supplier, ct);
        var locationName = await NameAsync(display, "StoreCell", doc.Location, ct);

        // Quantity x price per line, summed. Matches PurchaseGLEventHandler.
        decimal subTotal = 0m;
        foreach (var line in doc.Lines)
            subTotal += pricing.LineAmount(line.Quantity, line.UnitPrice);
        var taxTotal = 0m;
        var total = subTotal;

        var n = 0;
        foreach (var line in doc.Lines)
        {
            n++;
            var itemText = await NameAsync(display, "Item", line.Item, ct);
            var unitText = await NameAsync(display, "UnitOfMeasure", line.Unit, ct);
            table.Add(Row(
                doc.ID ?? "",
                DateText(doc.DocumentDate),
                "",
                supplierName,
                "",
                "",
                "",
                locationName,
                DateText(doc.DueDate),
                subTotal,
                taxTotal,
                total,
                n,
                itemText,
                line.Quantity,
                unitText,
                line.UnitPrice,
                pricing.LineAmount(line.Quantity, line.UnitPrice),
                0m,
                0m,
                "",
                ""));
        }

        // Without this an empty document renders a blank page — no header, no
        // totals — because the Detail band drives the whole report.
        if (n == 0)
        {
            table.Add(Row(
                doc.ID ?? "",
                DateText(doc.DocumentDate),
                "",
                supplierName,
                "",
                "",
                "",
                locationName,
                DateText(doc.DueDate),
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
