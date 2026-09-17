#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// ZATCA slice 1: structured TaxDocument + mock channel. XML, QR, CSID and
// HTTPS belong to a host connector, not this script.
public partial class SaudiEInvoice
{
    private static readonly Guid TaxDocumentType = Guid.Parse("cee833d7-fa4c-43fe-bef6-e95633ece906");
    private static readonly Guid SalesInvoiceType = Guid.Parse("34a1af4c-aeaf-48d1-8626-9a0a13b2d5c3");

    public Task<Guid?> EnsureForInvoiceAsync(Guid invoiceId)
        => EnsureAsync(invoiceId, "SalesRealization", "INVOICE");

    public Task<Guid?> EnsureForCreditNoteAsync(Guid creditNoteId)
        => EnsureAsync(creditNoteId, "SalesCreditNote", "CREDIT_NOTE");

    private async Task<Guid?> EnsureAsync(Guid sourceId, string sourceType, string documentType)
    {
        var dict = ScriptServices.Get<IDictionaryManager>();
        var settingsRows = await dict.GetRecordsAsync<LocalizationSaudiArabiaSettings>(null, 1);
        if (settingsRows.Count == 0 || !settingsRows[0].EInvoiceEnabled)
            return null;

        var docs = ScriptServices.Get<IDocumentManager>();
        var existing = await docs.QueryDocumentsAsync<TaxDocument>($"SourceDocumentId = '{sourceId}'");
        if (existing.Count > 0)
            return existing[0].MetaId;

        Guid customerId;
        Guid legalEntity;
        if (sourceType == "SalesRealization")
        {
            var invoice = await docs.GetDocumentAsync<SalesRealization>(sourceId);
            if (invoice is null) return null;
            customerId = invoice.Customer;
            legalEntity = invoice.LegalEntity;
        }
        else
        {
            var note = await docs.GetDocumentAsync<SalesCreditNote>(sourceId);
            if (note is null) return null;
            customerId = note.Customer;
            legalEntity = Guid.Empty;
        }

        var customer = customerId == Guid.Empty
            ? null
            : await ScriptServices.Get<IDictionaryManager<Customer>>().GetRecordAsync(customerId);
        var invoiceType = string.Equals(customer?.CustomerType, "B2B", StringComparison.OrdinalIgnoreCase)
            ? "Standard"
            : "Simplified";

        var envelope = await docs.NewDocumentAsync<TaxDocument>();
        envelope.LegalEntity = legalEntity;
        envelope.SourceDocumentType = sourceType;
        envelope.SourceDocumentId = sourceId;
        envelope.EInvoiceKind = documentType;
        envelope.InvoiceType = invoiceType;
        envelope.Uuid = Guid.NewGuid();
        await docs.SaveDocumentAsync(envelope);

        var posting = ScriptServices.Get<IDocumentPostingService>();
        await posting.SetSubtypeAsync(TaxDocumentType, envelope.MetaId, "Issued");

        if (sourceType == "SalesRealization")
        {
            await docs.UpdateDocumentAsync(SalesInvoiceType, sourceId,
                new Dictionary<string, object?>
                {
                    ["InvoiceUuid"] = envelope.Uuid,
                    ["ZatcaInvoiceType"] = invoiceType,
                });
        }

        var receipt = await ScriptServices.Get<ITaxAuthoritySubmitService>()
            .SubmitDocumentAsync(envelope.MetaId);
        var accepted = receipt.StartsWith("MOCK-OK:", StringComparison.Ordinal);
        var target = !accepted
            ? "Rejected"
            : invoiceType == "Standard" ? "Cleared" : "Reported";
        await posting.SetSubtypeAsync(TaxDocumentType, envelope.MetaId, target);
        return envelope.MetaId;
    }
}
