#nullable enable
using System;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Costing-model extension of Inventory: a stock-count recount UP
// opens a cost lot — the same uncompensated receipt as an adjustment
// surplus, and the same service.
//
// Stock-count peculiarity: its warehouse movements are written by the
// document's own OnBeforePost (it has access to the current on-hand that a
// Tx-script does not), not by a transactional script. The service does not
// care — it computes net from this document's already-written Stock
// movements, not from lines, and therefore works the same with both paths.
//
// A recount DOWN (shortage) does not get here: CostingIssue writes it off.
// Lot date is CountDate, the same date the document stamps Stock movements
// with. UtcNow here would put the layer on today for a back-dated count.
public partial class StockCountCostingEventHandler : TypedDocumentEventHandler<StockCount>
{
    public override async Task<EventResult> OnAfterPostAsync(StockCount document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Posted") return EventResult.Ok();

        var date = document.CountDate == default ? DateTime.UtcNow.Date : document.CountDate.Date;
        await context.GetService<ISurplusCostingService>()
            .CaptureSurplusAsync(document.MetaId, date);

        return EventResult.Ok();
    }
}
