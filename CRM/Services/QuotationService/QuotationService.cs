#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

public partial class QuotationService
{
    private static readonly Guid SalesQuotationType = Guid.Parse("73677103-31cf-4ff2-8a7e-7f4fc0a5bf6e");

    private readonly IDocumentManager _documents;
    private readonly IDocumentPostingService _posting;

    public QuotationService(IDocumentManager documents, IDocumentPostingService posting)
    {
        _documents = documents;
        _posting = posting;
    }

    private static ISalesLinePricing Pricing => ScriptServices.Get<ISalesLinePricing>();

    /// <summary>Create a Draft sales order from an issued quotation. Empty Guid —
    /// quotation missing, not issued, expired, or no lines. A second call returns
    /// the existing order (SourceQuotation).</summary>
    public async Task<Guid> ConvertToOrderAsync(Guid quotationId)
    {
        if (quotationId == Guid.Empty) return Guid.Empty;

        var existingCount = await _documents.CountDocumentsAsync<SalesOrder>(
            $"SourceQuotation = '{quotationId}'");
        if (existingCount > 0)
        {
            var found = (await _documents.QueryDocumentsAsync<SalesOrder>(
                $"SourceQuotation = '{quotationId}'")).FirstOrDefault();
            return found?.MetaId ?? Guid.Empty;
        }

        var quote = await _documents.GetDocumentAsync<SalesQuotation>(quotationId);
        if (quote == null || quote.Lines.Count == 0) return Guid.Empty;
        if (quote.Subtype != SalesQuotation.Subtypes.Issued
            && quote.Subtype != SalesQuotation.Subtypes.Converted)
            return Guid.Empty;

        if (HasExpiry(quote.ValidUntil) && quote.ValidUntil.Date < DateTime.UtcNow.Date)
            return Guid.Empty;

        var order = await _documents.NewDocumentAsync<SalesOrder>();
        order.Customer = quote.Customer;
        order.Location = quote.Location;
        order.Contract = quote.Contract;
        order.SourceQuotation = quote.MetaId;
        if (quote.Outlet != Guid.Empty)
            order.Outlet = quote.Outlet;
        if (quote.DeliveryDate != default)
            order.DeliveryDate = quote.DeliveryDate;
        if (quote.Contact != Guid.Empty)
            order.Contact = quote.Contact;
        if (quote.Agent != Guid.Empty)
            order.Agent = quote.Agent;
        if (quote.PaymentTerm != Guid.Empty)
            order.PaymentTerm = quote.PaymentTerm;
        if (quote.DeliveryTerm != Guid.Empty)
            order.DeliveryTerm = quote.DeliveryTerm;
        if (quote.DiscountPercent != 0m)
            order.DiscountPercent = quote.DiscountPercent;
        if (!string.IsNullOrWhiteSpace(quote.Notes))
            order.Notes = quote.Notes;

        foreach (var line in quote.Lines)
        {
            if (line.Quantity <= 0m) continue;
            var explanation = line.PriceExplanation ?? "";
            if (line.UnitPrice > 0m && !Pricing.IsManual(explanation, line.UnitPrice))
                explanation = "Вручную";
            order.Lines.Add(new SalesOrderLinesTablePartRow
            {
                Item = line.Item,
                Quantity = line.Quantity,
                Unit = line.Unit,
                UnitPrice = line.UnitPrice,
                PriceExplanation = explanation,
            });
        }

        if (order.Lines.Count == 0) return Guid.Empty;

        await _documents.SaveDocumentAsync(order);
        await _documents.AddLinkAsync(quote.MetaId, order.MetaId);
        if (quote.Subtype != SalesQuotation.Subtypes.Converted)
            await _posting.SetSubtypeAsync(SalesQuotationType, quote.MetaId, SalesQuotation.Subtypes.Converted);
        return order.MetaId;
    }

    private static bool HasExpiry(DateTime value) => value.Year >= 1902;
}
