#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// GLIntegration extension of Inventory: an inventory WRITE-OFF hits the
// general ledger — Dr inventory write-off / Cr inventory.
//
// Why: receipt debited the inventory account (PurchaseGLEventHandler), a sale
// credited it through COGS (SalesGLEventHandler), while breakage, shortage and
// other non-sale disposals never hit the books at all. Cost left the
// ItemCostFifo register and stayed on the inventory account forever: the books
// overstated inventory by everything written off over history.
//
// WHY NOT COGS. Cost of goods sold is the cost of what was SOLD, and gross
// margin is computed from it. Breakage and shortage are not a sale: dumping
// them into the same account would distort margin by the amount of the loss.
// So the write-off has its own account (InventoryWriteOffAccountCode). If
// unset — no posting, same as any other unconfigured leg: posting is
// best-effort and must not fail the document.
//
// The amount is NOT recalculated from lines: CostingIssue already wrote off
// cost, and SurplusCostingService opened the surplus lot; ItemCostFifo
// movements sit in the database with this document's DocumentMetaId. The FACT
// is read — the same approach as COGS posting, and for the same reason: the
// valuation method (FIFO/AVG) lives in settings, and repeating it here means
// guaranteed drift from inventory accounting.
//
// TWO LEGS. Write-off (cost > 0) — Dr loss / Cr inventory. Surplus (positive
// lot Amount) — Dr inventory / Cr surplus income. This is not a write-off
// reversal: a find must not wipe breakage on one account, or margin and the
// loss line stop being readable. Descriptions differ («Stock adjustment {id}»
// and «Stock adjustment surplus {id}») so GL idempotency does not collapse
// two facts into one posting. A zero lot — PostSurplusAsync returns null.
[ExtensionOf("StockAdjustment")]
public partial class StockAdjustmentGLEventHandler : TypedDocumentEventHandler<StockAdjustment>
{
    public override async Task<EventResult> OnAfterPostAsync(StockAdjustment document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Posted") return EventResult.Ok();

        var svc = context.GetService<IInventoryWriteOffGLService>();
        var wo = await svc.PostAsync(document.MetaId, document.Cell, document.DocumentDate,
                                    "Stock adjustment " + document.MetaId);
        var su = await svc.PostSurplusAsync(document.MetaId, document.Cell, document.DocumentDate,
                                           "Stock adjustment surplus " + document.MetaId);
        var links = context.GetService<IDocumentManager>();
        if (wo.HasValue) await links.AddLinkAsync(document.MetaId, wo.Value);
        if (su.HasValue) await links.AddLinkAsync(document.MetaId, su.Value);

        return EventResult.Ok();
    }
}
