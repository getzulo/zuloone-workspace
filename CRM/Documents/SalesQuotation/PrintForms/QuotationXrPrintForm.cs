#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Data;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

public partial class QuotationXrPrintForm : PrintFormBase
{
    public override SlimTable GetDataTemplate()
        => new SlimTable(Row("", "", "", "", "", "", "", "", 0m, 0m, 0m, 0, "", 0m, "", 0m, 0m));

    public override async Task<SlimTable> GetDataAsync(PrintFormContext context)
    {
        var table = new SlimTable("Report");
        var documents = context.GetService<IDocumentManager>();
        var quote = await documents.GetDocumentAsync<SalesQuotation>(context.RecordId);
        if (quote == null)
        {
            table.Add(Row("", "", "", "", "", "", "", "", 0m, 0m, 0m, 0, "", 0m, "", 0m, 0m));
            return table;
        }

        var display = context.GetService<IReferenceDisplay>();
        var pricing = context.GetService<IPricingService>();
        var ct = context.CancellationToken;
        var customer = await NameAsync(display, "Customer", quote.Customer, ct);
        var contract = await NameAsync(display, "SalesContract", quote.Contract, ct);
        var outlet = await NameAsync(display, "CustomerOutlet", quote.Outlet, ct);

        decimal subTotal = 0m;
        foreach (var line in quote.Lines)
            subTotal += pricing.LineAmount(line.Quantity, line.UnitPrice);
        var total = pricing.LineAmount(1m, subTotal, quote.DiscountPercent);

        var n = 0;
        foreach (var line in quote.Lines)
        {
            n++;
            var amount = pricing.LineAmount(line.Quantity, line.UnitPrice);
            var item = await NameAsync(display, "Item", line.Item, ct);
            var unit = await NameAsync(display, "UnitOfMeasure", line.Unit, ct);
            table.Add(Row(
                quote.ID ?? "",
                DateText(quote.DocumentDate),
                customer, contract, outlet,
                DateText(quote.ValidUntil),
                DateText(quote.DeliveryDate),
                quote.Notes ?? "",
                quote.DiscountPercent, subTotal, total,
                n, item, line.Quantity, unit, line.UnitPrice, amount));
        }

        if (n == 0)
        {
            table.Add(Row(
                quote.ID ?? "",
                DateText(quote.DocumentDate),
                customer, contract, outlet,
                DateText(quote.ValidUntil),
                DateText(quote.DeliveryDate),
                quote.Notes ?? "",
                quote.DiscountPercent, subTotal, total,
                0, "", 0m, "", 0m, 0m));
        }

        return table;
    }

    private static object Row(
        string number, string documentDate, string customer, string contract, string outlet,
        string validUntil, string deliveryDate, string notes,
        decimal discountPercent, decimal subTotal, decimal total,
        int lineNo, string item, decimal quantity, string unit, decimal unitPrice, decimal amount)
        => new
        {
            Number = number,
            DocumentDate = documentDate,
            Customer = customer,
            Contract = contract,
            Outlet = outlet,
            ValidUntil = validUntil,
            DeliveryDate = deliveryDate,
            Notes = notes,
            DiscountPercent = discountPercent,
            SubTotal = subTotal,
            Total = total,
            LineNo = lineNo,
            Item = item,
            Quantity = quantity,
            Unit = unit,
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
