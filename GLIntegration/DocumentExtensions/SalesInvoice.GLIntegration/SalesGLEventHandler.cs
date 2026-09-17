#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// SalesRealization Chain of Command link from GLIntegration: on issue, the sale
// posts to the general ledger as TWO journals — revenue (Dr AR / Cr revenue)
// and cost of sales (Dr COGS / Cr inventory).
//
// Two journals, not one four-line entry: PostAsync posts a balanced PAIR.
// That split is correct — revenue recognition and COGS live by different
// rules (revenue is always there; an item with no layers has no cost).
// Cost of zero — the second journal is skipped, invoice still posts.
//
// Class name is OWN, not the owner's. Naming it after the Sales handler
// does not extend the chain — it REPLACES the Sales handler and its
// on-hand check. CoC links are separate classes; EventService runs every
// layer. A colliding class name used to swallow this posting when Sales
// added its own stock check.
//
// The event stays thin: amount, legal entity, then GeneralLedgerService.
// No profile — GetSettingsAsync returns null, no journal. A real posting
// failure must not be swallowed: OnAfterPost does not roll back the
// document, and an empty catch hid the cause from the log.
public partial class SalesGLEventHandler : TypedDocumentEventHandler<SalesRealization>
{
    public override async Task<EventResult> OnAfterPostAsync(SalesRealization document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Issued") return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();

        var jeId = await PostToLedgerAsync(document, context);
        if (jeId.HasValue)
            await docs.AddLinkAsync(document.MetaId, jeId.Value);

        var cogsId = await PostCostOfSalesAsync(document, context);
        if (cogsId.HasValue)
            await docs.AddLinkAsync(document.MetaId, cogsId.Value);

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(SalesRealization header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        // Header-event lines are empty — reload the full document.
        var inv = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesRealization>(header.MetaId);
        if (inv == null) return null;

        // Same formula as receivable, revenue, VAT and points: otherwise the
        // invoice discount splits registers from the ledger. IPricingService
        // no longer breaks across the assembly boundary.
        var pricing = context.GetService<IPricingService>();
        var total = inv.Lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice, inv.DiscountPercent));

        var le = await ResolveLegalEntityAsync(inv, context);
        if (le == null) return null;

        return await gl.PostAsync(
            inv.DocumentDate, le.MetaId, le.Currency, total,
            settings.ArAccountCode, settings.RevenueAccountCode,
            "Sales invoice " + header.MetaId,
            "Дебиторка по продаже", "Выручка от продажи");
    }

    /// <summary>
    /// Cost of sales: Dr COGS / Cr inventory.
    ///
    /// Amount is NOT recomputed from invoice lines — it is already posted.
    /// CostingIssue on Stock writes it ("qty down — cost written off"); by
    /// this event ItemCostFifo movements for our DocumentMetaId are in the
    /// database. Read the FACT of the issue, not a second calculation:
    /// otherwise FIFO/AVG from CostingSettings would be duplicated here and
    /// stock would diverge from the ledger the day the method changes.
    ///
    /// Issue Amount is negative (the engine puts layer cost there instead of
    /// the zero we passed) — the journal uses the absolute value. No layers
    /// (item was booked by a raw register move, not a receipt) — nothing to
    /// write off, amount zero, no journal.
    /// </summary>
    private async Task<Guid?> PostCostOfSalesAsync(SalesRealization header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        var moves = await context.GetService<ITotalsManager>()
            .QueryMovementsAsync("ItemCostFifo", $"[DocumentMetaId] = '{header.MetaId}'");

        var cost = 0m;
        foreach (var row in moves)
            if (row.TryGetValue("Amount", out var amount) && amount != null)
                cost -= Convert.ToDecimal(amount);
        if (cost <= 0m) return null;

        var inv = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesRealization>(header.MetaId);
        if (inv == null) return null;

        var le = await ResolveLegalEntityAsync(inv, context);
        if (le == null) return null;

        return await gl.PostAsync(
            inv.DocumentDate, le.MetaId, le.Currency, cost,
            settings.CogsAccountCode, settings.InventoryAccountCode,
            "Cost of sales " + header.MetaId,
            "Себестоимость продажи", "Выбытие запасов");
    }

    /// <summary>Seller legal entity comes from the invoice: issue stamps it
    /// (<c>SalesInvoiceEventHandler.OnBeforePost</c>) via Cell → Zone →
    /// Warehouse → Division → LegalEntity. Read the field, do not walk the
    /// chain again — not to save four reads: the invoice may have been issued
    /// for another legal entity (agent sale from a foreign warehouse), and
    /// the journal must land where tax and the invoice already are. Empty —
    /// org structure is not filled, posting is skipped.</summary>
    private static async Task<LegalEntity?> ResolveLegalEntityAsync(SalesRealization invoice, EventContext context)
    {
        if (invoice.LegalEntity == Guid.Empty) return null;
        return await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(invoice.LegalEntity);
    }
}
