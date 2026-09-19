#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Data;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Same Annex 2 printout as ZatcaInvoiceXr, for a posted credit note.
// Standard stays gated by BuyerReleaseBlockAsync until TaxDocument is Cleared.
public partial class ZatcaCreditNoteXrPrintForm : PrintFormBase
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
        var note = await documents.GetDocumentAsync<SalesCreditNote>(context.RecordId);
        if (note == null)
        {
            table.Add(Empty());
            return table;
        }

        var block = await context.GetService<ISaudiEInvoice>().BuyerReleaseBlockAsync(note.MetaId);
        if (!string.IsNullOrEmpty(block))
            throw new InvalidOperationException(block);

        var display = context.GetService<IReferenceDisplay>();
        var pricing = context.GetService<IPricingService>();
        var taxes = context.GetService<ITaxService>();
        var data = context.GetService<IDataService>();
        var ct = context.CancellationToken;

        var row = await data.GetByIdAsync("SalesCreditNote", note.MetaId);
        var qr = Text(row, "QrCode");
        var zatcaType = Text(row, "ZatcaInvoiceType");
        var title = string.Equals(zatcaType, "Standard", StringComparison.OrdinalIgnoreCase)
            ? "Tax Credit Note / إشعار دائن ضريبي"
            : "Simplified Tax Credit Note / إشعار دائن ضريبي مبسط";

        var seller = await NameAsync(display, "LegalEntity", note.LegalEntity, ct);
        var sellerRow = note.LegalEntity == Guid.Empty
            ? null
            : await data.GetByIdAsync("LegalEntity", note.LegalEntity);
        var sellerVat = Text(sellerRow, "TaxRegistrationNumber");
        var sellerCrn = Text(sellerRow, "CommercialRegistration");
        var sellerAddress = await AddressAsync(data, display, Guid1(sellerRow, "LegalAddress"), ct);

        var customer = await NameAsync(display, "Customer", note.Customer, ct);
        var buyerRow = note.Customer == Guid.Empty
            ? null
            : await data.GetByIdAsync("Customer", note.Customer);
        var buyerVat = Text(buyerRow, "TaxRegistrationNumber");
        var buyerAddress = await AddressAsync(data, display, Guid1(buyerRow, "Address"), ct);

        decimal net = 0m;
        decimal tax = 0m;
        var lineNets = new List<decimal>();
        var lineTaxes = new List<decimal>();
        foreach (var line in note.Lines)
        {
            var lineNet = pricing.LineAmount(line.Quantity, line.UnitPrice);
            var lineTax = note.TaxRateApplied > 0m
                ? taxes.CalculateTax(lineNet, note.TaxRateApplied)
                : 0m;
            lineNets.Add(lineNet);
            lineTaxes.Add(lineTax);
            net += lineNet;
            tax += lineTax;
        }
        var gross = net + tax;
        var ratePercent = note.TaxRateApplied * 100m;

        var n = 0;
        foreach (var line in note.Lines)
        {
            var lineNet = lineNets[n];
            var lineTax = lineTaxes[n];
            n++;
            table.Add(Row(
                title, note.ID ?? "", DateText(note.DocumentDate),
                seller, sellerVat, sellerCrn, sellerAddress,
                customer, buyerVat, buyerAddress,
                qr, zatcaType, "",
                ratePercent, net, tax, gross, "Amount includes VAT / المبلغ شامل الضريبة",
                n, await NameAsync(display, "Item", line.Item, ct), line.Quantity,
                "",
                line.UnitPrice, lineNet, lineTax, lineNet + lineTax));
        }

        if (n == 0)
        {
            table.Add(Row(
                title, note.ID ?? "", DateText(note.DocumentDate),
                seller, sellerVat, sellerCrn, sellerAddress,
                customer, buyerVat, buyerAddress,
                qr, zatcaType, "",
                ratePercent, net, tax, gross, "Amount includes VAT / المبلغ شامل الضريبة",
                0, "", 0m, "", 0m, 0m, 0m, 0m));
        }

        return table;
    }

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
