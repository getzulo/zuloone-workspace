#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Data;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// ZATCA printed tax invoice.
//
// The field list is not invented: it is the "Visibility on Invoice (such as
// PDF, printout, any other human readable form)" column of the E-Invoicing
// Implementation Resolution, Annex 2. Required there means it must appear on
// paper — separately from the Obligation column, which governs the XML. Hence
// the document title (1.1), IRN (2.1), QR (2.4), issue date (3.1), seller name
// /address/VAT/CRN (4.1-4.4), buyer name/address/VAT (5.1-5.3), per-line
// description/price/quantity/net/VAT rate/VAT/gross (7.1-7.11) and the totals
// with the literal "Amount includes VAT" (8.3-8.5).
//
// UUID, previous hash, ICV and issue time are NOT required on the printout —
// they are mandatory in the XML only, so they are deliberately absent here.
//
// Totals must equal what is sealed inside the QR. SaudiEInvoice computes
//   net  = Σ LineAmount(qty, price, DiscountPercent)     -- per line
//   tax  = Round(net * TaxRateApplied, 2, AwayFromZero)
// and ITaxService.CalculateTax rounds the same way at the default AmountScale.
// Any other rounding here would print totals that disagree with tags 4 and 5.
public partial class ZatcaInvoiceXrPrintForm : PrintFormBase
{
    public override SlimTable GetDataTemplate()
        => new SlimTable(Row(
            "", "", "", "", "", "", "", "", "", "", "", "", "",
            0m, 0m, 0m, 0m, "",
            0, "", 0m, "", 0m, 0m, 0m, 0m));

    public override async Task<SlimTable> GetDataAsync(PrintFormContext context)
    {
        var table = new SlimTable("Report");
        var documents = context.GetService<IDocumentManager>();
        var invoice = await documents.GetDocumentAsync<SalesRealization>(context.RecordId);
        if (invoice == null)
        {
            table.Add(Empty());
            return table;
        }

        var display = context.GetService<IReferenceDisplay>();
        var pricing = context.GetService<IPricingService>();
        var taxes = context.GetService<ITaxService>();
        var data = context.GetService<IDataService>();
        var ct = context.CancellationToken;

        // The ZATCA fields live on the Saudi document EXTENSION. The generated
        // SalesRealization type does not carry them, so they are read off the
        // physical row — the same way ZatcaEInvoiceTest reads QrCode back.
        var row = await data.GetByIdAsync("SalesRealization", invoice.MetaId);
        var qr = Text(row, "QrCode");
        var zatcaType = Text(row, "ZatcaInvoiceType");

        // Requirement 1.1: the title is part of the document, not decoration.
        var title = string.Equals(zatcaType, "Standard", StringComparison.OrdinalIgnoreCase)
            ? "Tax Invoice / فاتورة ضريبية"
            : "Simplified Tax Invoice / فاتورة ضريبية مبسطة";

        var seller = await NameAsync(display, "LegalEntity", invoice.LegalEntity, ct);
        var sellerRow = invoice.LegalEntity == Guid.Empty
            ? null
            : await data.GetByIdAsync("LegalEntity", invoice.LegalEntity);
        var sellerVat = Text(sellerRow, "TaxRegistrationNumber");
        var sellerCrn = Text(sellerRow, "CommercialRegistration");
        var sellerAddress = await AddressAsync(data, display, Guid1(sellerRow, "LegalAddress"), ct);

        var customer = await NameAsync(display, "Customer", invoice.Customer, ct);
        var buyerRow = invoice.Customer == Guid.Empty
            ? null
            : await data.GetByIdAsync("Customer", invoice.Customer);
        var buyerVat = Text(buyerRow, "TaxRegistrationNumber");
        var buyerAddress = await AddressAsync(data, display, Guid1(buyerRow, "Address"), ct);

        // Totals ACCUMULATED FROM THE LINES, matching SaudiEInvoice. Rounding the
        // sum and summing the rounded lines are not the same number — three
        // lines of 0.10 at 15% give 0.06 by line and 0.05 by total — and the
        // printed column then failed to add up to its own printed total, while
        // QR tags 4 and 5 carried the third variant.
        decimal net = 0m;
        decimal tax = 0m;
        var lineNets = new List<decimal>();
        var lineTaxes = new List<decimal>();
        foreach (var line in invoice.Lines)
        {
            var lineNet = pricing.LineAmount(line.Quantity, line.UnitPrice, invoice.DiscountPercent);
            var lineTax = invoice.TaxRateApplied > 0m
                ? taxes.CalculateTax(lineNet, invoice.TaxRateApplied)
                : 0m;
            lineNets.Add(lineNet);
            lineTaxes.Add(lineTax);
            net += lineNet;
            tax += lineTax;
        }
        var gross = net + tax;
        var ratePercent = invoice.TaxRateApplied * 100m;

        var n = 0;
        foreach (var line in invoice.Lines)
        {
            var lineNet = lineNets[n];
            var lineTax = lineTaxes[n];
            n++;
            table.Add(Row(
                title, invoice.ID ?? "", DateText(invoice.DocumentDate),
                seller, sellerVat, sellerCrn, sellerAddress,
                customer, buyerVat, buyerAddress,
                qr, zatcaType, invoice.Notes ?? "",
                ratePercent, net, tax, gross, "Amount includes VAT / المبلغ شامل الضريبة",
                n, await NameAsync(display, "Item", line.Item, ct), line.Quantity,
                await NameAsync(display, "UnitOfMeasure", line.Unit, ct),
                line.UnitPrice, lineNet, lineTax, lineNet + lineTax));
        }

        if (n == 0)
        {
            table.Add(Row(
                title, invoice.ID ?? "", DateText(invoice.DocumentDate),
                seller, sellerVat, sellerCrn, sellerAddress,
                customer, buyerVat, buyerAddress,
                qr, zatcaType, invoice.Notes ?? "",
                ratePercent, net, tax, gross, "Amount includes VAT / المبلغ شامل الضريبة",
                0, "", 0m, "", 0m, 0m, 0m, 0m));
        }

        return table;
    }

    /// <summary>
    /// One address line. Resolves City through the City dictionary rather than
    /// reusing Address.Name: the UBL builder emits addr.Name as cbc:CityName,
    /// which is the address line, not the city — a bug not worth copying onto
    /// paper.
    /// </summary>
    private static async Task<string> AddressAsync(
        IDataService data, IReferenceDisplay display, Guid addressId, CancellationToken ct)
    {
        if (addressId == Guid.Empty) return "";
        var a = await data.GetByIdAsync("Address", addressId);
        if (a == null) return "";
        var parts = new List<string>();
        void Add(string value) { if (!string.IsNullOrWhiteSpace(value)) parts.Add(value.Trim()); }
        Add(Text(a, "Building"));
        Add(Text(a, "Street"));
        Add(Text(a, "District"));
        Add(await NameAsync(display, "City", Guid1(a, "City"), ct));
        Add(Text(a, "PostalCode"));
        Add(await NameAsync(display, "Country", Guid1(a, "Country"), ct));
        return parts.Count > 0 ? string.Join(", ", parts) : Text(a, "Name");
    }

    private static string Text(IDictionary<string, object?>? row, string column)
        => row != null && row.TryGetValue(column, out var v) ? Convert.ToString(v) ?? "" : "";

    private static Guid Guid1(IDictionary<string, object?>? row, string column)
    {
        if (row == null || !row.TryGetValue(column, out var v) || v == null) return Guid.Empty;
        return v is Guid g ? g : Guid.TryParse(Convert.ToString(v), out var p) ? p : Guid.Empty;
    }

    private static object Empty()
        => Row("", "", "", "", "", "", "", "", "", "", "", "", "",
               0m, 0m, 0m, 0m, "", 0, "", 0m, "", 0m, 0m, 0m, 0m);

    private static object Row(
        string title, string number, string issueDate,
        string seller, string sellerVat, string sellerCrn, string sellerAddress,
        string buyer, string buyerVat, string buyerAddress,
        string qrCode, string invoiceType, string notes,
        decimal taxRatePercent, decimal netTotal, decimal taxTotal, decimal grossTotal, string inclusiveNote,
        int lineNo, string item, decimal quantity, string unit,
        decimal unitPrice, decimal lineNet, decimal lineTax, decimal lineGross)
        => new
        {
            Title = title,
            Number = number,
            IssueDate = issueDate,
            Seller = seller,
            SellerVat = sellerVat,
            SellerCrn = sellerCrn,
            SellerAddress = sellerAddress,
            Buyer = buyer,
            BuyerVat = buyerVat,
            BuyerAddress = buyerAddress,
            QrCode = qrCode,
            InvoiceType = invoiceType,
            Notes = notes,
            TaxRatePercent = taxRatePercent,
            NetTotal = netTotal,
            TaxTotal = taxTotal,
            GrossTotal = grossTotal,
            InclusiveNote = inclusiveNote,
            LineNo = lineNo,
            Item = item,
            Quantity = quantity,
            Unit = unit,
            UnitPrice = unitPrice,
            LineNet = lineNet,
            LineTax = lineTax,
            LineGross = lineGross,
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
