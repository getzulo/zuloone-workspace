#nullable enable
using System;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// After Cleared/Reported the e-invoice is frozen. Corrections are a new
// TaxDocument (credit note), not an edit of the accepted one.
public partial class TaxDocumentEventHandler : TypedDocumentEventHandler<TaxDocument>
{
    public override async Task<EventResult> OnBeforeSaveAsync(TaxDocument header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;
        if (isNew) return EventResult.Ok();

        var stored = await context.GetService<IDocumentManager>().GetDocumentAsync<TaxDocument>(header.MetaId);
        if (stored is null) return EventResult.Ok();
        if (stored.Subtype == "Cleared" || stored.Subtype == "Reported")
            return EventResult.Cancel(
                "После clearance фактура неизменна — исправление только новым документом (кредит-нота)");
        return EventResult.Ok();
    }
}
