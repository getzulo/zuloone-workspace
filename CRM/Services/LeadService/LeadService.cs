#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;

public partial class LeadService
{
    private static readonly Guid SalesLeadType = Guid.Parse("64daae1f-09b3-47f7-b5b5-c5544a102d98");

    private readonly IDocumentManager _documents;
    private readonly IDocumentPostingService _posting;

    public LeadService(IDocumentManager documents, IDocumentPostingService posting)
    {
        _documents = documents;
        _posting = posting;
    }

    /// <summary>Draft quotation from a qualified lead. Empty Guid — missing
    /// commercial fields or wrong subtype. Second call returns the existing КП.</summary>
    public async Task<Guid> CreateQuotationAsync(Guid leadId)
    {
        if (leadId == Guid.Empty) return Guid.Empty;

        var existingCount = await _documents.CountDocumentsAsync<SalesQuotation>(
            $"SourceLead = '{leadId}'");
        if (existingCount > 0)
        {
            var found = (await _documents.QueryDocumentsAsync<SalesQuotation>(
                $"SourceLead = '{leadId}'")).FirstOrDefault();
            return found?.MetaId ?? Guid.Empty;
        }

        var lead = await _documents.GetDocumentAsync<SalesLead>(leadId);
        if (lead == null) return Guid.Empty;
        if (lead.Subtype != SalesLead.Subtypes.Qualified
            && lead.Subtype != SalesLead.Subtypes.Quoted)
            return Guid.Empty;
        if (lead.Customer == Guid.Empty || lead.Contract == Guid.Empty
            || lead.Location == Guid.Empty || lead.DeliveryDate.Year < 1902)
            return Guid.Empty;

        var quote = await _documents.NewDocumentAsync<SalesQuotation>();
        quote.Customer = lead.Customer;
        quote.Contract = lead.Contract;
        quote.Location = lead.Location;
        quote.DeliveryDate = lead.DeliveryDate;
        quote.SourceLead = lead.MetaId;
        if (lead.Outlet != Guid.Empty)
            quote.Outlet = lead.Outlet;
        if (lead.Contact != Guid.Empty)
            quote.Contact = lead.Contact;
        if (lead.Agent != Guid.Empty)
            quote.Agent = lead.Agent;
        if (!string.IsNullOrWhiteSpace(lead.Notes))
            quote.Notes = lead.Notes;

        await _documents.SaveDocumentAsync(quote);
        await _documents.AddLinkAsync(lead.MetaId, quote.MetaId);
        if (lead.Subtype != SalesLead.Subtypes.Quoted)
            await _posting.SetSubtypeAsync(SalesLeadType, lead.MetaId, SalesLead.Subtypes.Quoted);
        return quote.MetaId;
    }
}
