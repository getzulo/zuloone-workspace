#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Data;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Sales invoice. Totals are NOT stored on the document — they are recomputed
// here, and they have to agree with two other places or the paper disagrees
// with the books:
//
//   SalesReceivableTx   net += LineAmount(qty, price, DiscountPercent)   per line
//   SaudiEInvoice       the same, and the QR then carries the result
//
// So the discount is applied PER LINE, not once on the sum. OrderXr does the
// latter, which is harmless for an order because an order is never posted;
// copying it here would round differently and put the invoice out of step with
// the ledger and with the QR on the Saudi form.
public partial class InvoiceXrPrintForm : PrintFormBase
{
    public override SlimTable GetDataTemplate()
        => new SlimTable(Row(
            "", "", "", "", "", "", "", "", "", "",
            0m, 0m, 0m, 0m, 0m,
            0, "", 0m, "", 0m, 0m));

    public override async Task<SlimTable> GetDataAsync(PrintFormContext context)
    {
        var table = new SlimTable("Report");
        var documents = context.GetService<IDocumentManager>();
        var invoice = await documents.GetDocumentAsync<SalesRealization>(context.RecordId);
        if (invoice == null)
        {
            table.Add(Row("", "", "", "", "", "", "", "", "", "",
                0m, 0m, 0m, 0m, 0m, 0, "", 0m, "", 0m, 0m));
            return table;
        }

        var block = await context.GetService<IEInvoiceRelease>()
            .BuyerReleaseBlockAsync(invoice.MetaId);
        if (!string.IsNullOrEmpty(block))
            throw new InvalidOperationException(block);

        var display = context.GetService<IReferenceDisplay>();
        var pricing = context.GetService<IPricingService>();
        var taxes = context.GetService<ITaxService>();
        var ct = context.CancellationToken;

        var seller = await NameAsync(display, "LegalEntity", invoice.LegalEntity, ct);
        var customer = await NameAsync(display, "Customer", invoice.Customer, ct);
        var contract = await NameAsync(display, "SalesContract", invoice.Contract, ct);
        var outlet = await NameAsync(display, "CustomerOutlet", invoice.Outlet, ct);
        var location = await NameAsync(display, "StoreCell", invoice.Location, ct);
        var paymentTerm = await NameAsync(display, "PaymentTerm", invoice.PaymentTerm, ct);
        var dueText = DateText(invoice.DueDate);
        if (!string.IsNullOrEmpty(dueText))
            paymentTerm = string.IsNullOrEmpty(paymentTerm) ? dueText : paymentTerm + " · " + dueText;
        var deliveryTerm = await NameAsync(display, "DeliveryTerm", invoice.DeliveryTerm, ct);

        decimal net = 0m;
        foreach (var line in invoice.Lines)
            net += pricing.LineAmount(line.Quantity, line.UnitPrice, invoice.DiscountPercent);
        var tax = invoice.TaxRateApplied > 0m ? taxes.CalculateTax(net, invoice.TaxRateApplied) : 0m;
        var total = net + tax;

        // TaxRateApplied is a fraction (0.15); the paper shows a percentage.
        var ratePercent = invoice.TaxRateApplied * 100m;

        var n = 0;
        foreach (var line in invoice.Lines)
        {
            n++;
            var amount = pricing.LineAmount(line.Quantity, line.UnitPrice, invoice.DiscountPercent);
            var item = await NameAsync(display, "Item", line.Item, ct);
            var unit = await NameAsync(display, "UnitOfMeasure", line.Unit, ct);
            table.Add(Row(
                invoice.ID ?? "", DateText(invoice.DocumentDate),
                seller, customer, contract, outlet, location,
                paymentTerm, deliveryTerm, invoice.Notes ?? "",
                invoice.DiscountPercent, ratePercent, net, tax, total,
                n, item, line.Quantity, unit, line.UnitPrice, amount));
        }

        if (n == 0)
        {
            table.Add(Row(
                invoice.ID ?? "", DateText(invoice.DocumentDate),
                seller, customer, contract, outlet, location,
                paymentTerm, deliveryTerm, invoice.Notes ?? "",
                invoice.DiscountPercent, ratePercent, net, tax, total,
                0, "", 0m, "", 0m, 0m));
        }

        return table;
    }

    private static object Row(
        string number, string documentDate, string seller, string customer, string contract,
        string outlet, string location, string paymentTerm, string deliveryTerm, string notes,
        decimal discountPercent, decimal taxRatePercent, decimal subTotal, decimal taxAmount, decimal total,
        int lineNo, string item, decimal quantity, string unit, decimal unitPrice, decimal amount)
        => new
        {
            Number = number,
            DocumentDate = documentDate,
            Seller = seller,
            Customer = customer,
            Contract = contract,
            Outlet = outlet,
            Location = location,
            PaymentTerm = paymentTerm,
            DeliveryTerm = deliveryTerm,
            Notes = notes,
            DiscountPercent = discountPercent,
            TaxRatePercent = taxRatePercent,
            SubTotal = subTotal,
            TaxAmount = taxAmount,
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
