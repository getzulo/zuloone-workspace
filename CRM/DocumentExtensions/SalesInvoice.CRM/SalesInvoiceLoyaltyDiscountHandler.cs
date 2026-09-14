#nullable enable
using System.Linq;

namespace ZuloOne.Runtime.Generated;

// CRM extension of SalesInvoice: on issue, stamp the customer's loyalty-tier
// discount on the header.
//
// WHERE this runs. Not on post — on the SUBTYPE write. Subtype is owned by a
// separate engine: SaveDocumentAsync never writes it (platform-owned field),
// SetSubtypeAsync does a one-column UPDATE — we hook that before-event.
// WriteBack puts the discount in the same UPDATE, so it is in the database
// BEFORE the engine builds movements.
//
// OnBeforePost cannot set the discount: the event instance is not written
// (no WriteBack on post events), and a header-only write from there no
// longer reaches the movements. The discount would apply only to the tax
// base in OnAfterPost (which reloads the document) — receivable, revenue,
// VAT and points would ignore it. That ledger split is why the discount
// lives on the header.
//
// THE DOCUMENT IS RELOADED. A partial update event carries ONLY the columns
// being written — here that is Subtype; Customer and the current discount
// are zeros. Do not judge the document from that instance.
//
// Tier comes from the ACCUMULATED points balance, not a field on the
// customer, so it cannot drift from actual points. Balance is read before
// this invoice awards points — the invoice is not posted yet.
public partial class SalesInvoiceLoyaltyDiscountHandler : TypedDocumentEventHandler<SalesInvoice>
{
    public override async Task<EventResult> OnBeforeSaveAsync(SalesInvoice header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;
        if (isNew || header.Subtype != "Issued" || header.MetaId == Guid.Empty) return EventResult.Ok();

        var stored = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesInvoice>(header.MetaId);
        if (stored == null || stored.Customer == Guid.Empty) return EventResult.Ok();

        // A hand-entered discount is a person's decision (a deal). Loyalty
        // tier does not overwrite it, same as the seller legal entity.
        if (stored.DiscountPercent > 0m) return EventResult.Ok();

        var balance = await context.GetService<ITotalsManager>().GetBalanceAsync("LoyaltyPoints",
            new Dictionary<string, object?> { ["Customer"] = stored.Customer });
        var points = balance != null && balance.TryGetValue("Points", out var raw) && raw != null
            ? Convert.ToDecimal(raw)
            : 0m;

        var reached = (await context.GetService<IDictionaryManager<LoyaltyTier>>()
                .GetRecordsAsync($"MinPoints <= {points}"))
            .OrderByDescending(t => t.MinPoints)
            .FirstOrDefault();

        // No tier (empty ladder or not enough points) — no discount, not an
        // error: the invoice issues at full price.
        if (reached == null || reached.DiscountPercent <= 0m) return EventResult.Ok();

        header.DiscountPercent = reached.DiscountPercent;
        return EventResult.Ok();
    }
}
