#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// GLIntegration extension of Inventory: counted shortage and surplus
// hit the general ledger. Same service as stock adjustment:
// the handler only decides WHEN.
//
// WHY THIS IS A SEPARATE LINK. Stock count is the main way to find shrinkage
// and a find. Shortage: the CostingIssue driver takes cost off ItemCostFifo —
// without a ledger leg the inventory account was overstated for the entire
// history. Surplus: Costing opens a lot (positive Amount) — without a ledger
// leg the inventory account was understated, and the found goods did not
// exist in the books.
//
// DATE — CountDate, not DocumentDate: that is what the register movements
// are dated with, and the posting must land in the same period.
//
// TWO LEGS, TWO DESCRIPTIONS. Write-off and surplus are different facts;
// GeneralLedgerService.PostAsync idempotency is keyed on the description, so
// «Stock count {id}» and «Stock count surplus {id}» do not overwrite each
// other. Surplus credits ITS OWN income account, it does not reverse the
// write-off: margin and losses stay clean.
// A zero lot (no purchase history) — PostSurplusAsync returns null.
// This link runs after Costing (the layer is already opened); the amount is
// read from ItemCostFifo.
[ExtensionOf("StockCount")]
public partial class StockCountGLEventHandler : TypedDocumentEventHandler<StockCount>
{
    public override async Task<EventResult> OnAfterPostAsync(StockCount document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Posted") return EventResult.Ok();

        var date = document.CountDate == default ? DateTime.UtcNow.Date : document.CountDate;
        var svc = context.GetService<IInventoryWriteOffGLService>();
        var wo = await svc.PostAsync(document.MetaId, document.Cell, date,
                                    "Stock count " + document.MetaId);
        var su = await svc.PostSurplusAsync(document.MetaId, document.Cell, date,
                                           "Stock count surplus " + document.MetaId);
        var links = context.GetService<IDocumentManager>();
        if (wo.HasValue) await links.AddLinkAsync(document.MetaId, wo.Value);
        if (su.HasValue) await links.AddLinkAsync(document.MetaId, su.Value);

        return EventResult.Ok();
    }
}
