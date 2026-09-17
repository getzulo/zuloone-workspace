#nullable enable
using System;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Costing-model extension of Inventory: an adjustment surplus opens a
// cost lot. The dependency direction must be exactly this — Costing
// depends on Inventory; the reverse cannot exist (that would be a cycle),
// so the handler lives here, not on the document itself.
//
// OnAfterPost, not a transactional script: the price is taken from the
// current lot balance, and that is a DB read — it does not fit in
// synchronous, pure GetTransactions. By this point warehouse movements
// are already written, and the service computes net from them.
//
// A shortage (net minus) does not get here: CostingIssue writes it off.
// Lot date is the document's DocumentDate, not the day «Post» was clicked.
[ExtensionOf("StockAdjustment")]
public partial class StockAdjustmentCostingEventHandler : TypedDocumentEventHandler<StockAdjustment>
{
    public override async Task<EventResult> OnAfterPostAsync(StockAdjustment document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Posted") return EventResult.Ok();

        var date = document.DocumentDate == default ? DateTime.UtcNow.Date : document.DocumentDate.Date;
        await context.GetService<ISurplusCostingService>()
            .CaptureSurplusAsync(document.MetaId, date);

        return EventResult.Ok();
    }
}
