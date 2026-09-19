#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Data;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Goods return paper — stock and net receivable, not a tax invoice.
// VAT reverse is SalesCreditNote; this form does not wait for clearance.
public partial class ReturnXrPrintForm : PrintFormBase
{
    public override SlimTable GetDataTemplate()
        => new SlimTable(Row("", "", "", "", "", "", "", 0m, 0m, 0, "", 0m, 0m, 0m));

    public override async Task<SlimTable> GetDataAsync(PrintFormContext context)
    {
        var table = new SlimTable("Report");
        var documents = context.GetService<IDocumentManager>();
        var doc = await documents.GetDocumentAsync<SalesReturn>(context.RecordId);
        if (doc == null)
        {
            table.Add(Row("", "", "", "", "", "", "", 0m, 0m, 0, "", 0m, 0m, 0m));
            return table;
        }

        var display = context.GetService<IReferenceDisplay>();
        var pricing = context.GetService<IPricingService>();
        var ct = context.CancellationToken;
        var customer = await NameAsync(display, "Customer", doc.Customer, ct);
        var contract = await NameAsync(display, "SalesContract", doc.Contract, ct);
        var outlet = await NameAsync(display, "CustomerOutlet", doc.Outlet, ct);
        var location = await NameAsync(display, "StoreCell", doc.Location, ct);
        var original = await NameAsync(display, "SalesRealization", doc.OriginalInvoice, ct);

        decimal subTotal = 0m;
        foreach (var line in doc.Lines)
            subTotal += pricing.LineAmount(line.Quantity, line.UnitPrice);

        var n = 0;
        foreach (var line in doc.Lines)
        {
            n++;
            var amount = pricing.LineAmount(line.Quantity, line.UnitPrice);
            var item = await NameAsync(display, "Item", line.Item, ct);
            table.Add(Row(
                doc.ID ?? "",
                DateText(doc.DocumentDate),
                customer, contract, outlet, location, original,
                subTotal, subTotal,
                n, item, line.Quantity, line.UnitPrice, amount));
        }

        if (n == 0)
        {
            table.Add(Row(
                doc.ID ?? "",
                DateText(doc.DocumentDate),
                customer, contract, outlet, location, original,
                subTotal, subTotal,
                0, "", 0m, 0m, 0m));
        }

        return table;
    }

    private static object Row(
        string number, string documentDate, string customer, string contract, string outlet,
        string location, string notes,
        decimal subTotal, decimal total,
        int lineNo, string item, decimal quantity, decimal unitPrice, decimal amount)
        => new
        {
            Number = number,
            DocumentDate = documentDate,
            Customer = customer,
            Contract = contract,
            Outlet = outlet,
            Location = location,
            Notes = notes,
            SubTotal = subTotal,
            Total = total,
            LineNo = lineNo,
            Item = item,
            Quantity = quantity,
            Unit = "",
            UnitPrice = unitPrice,
            Amount = amount,
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
