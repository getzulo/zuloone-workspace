#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// GLIntegration extension of Inventory: a warehouse ISSUE hits the
// general ledger — Dr inventory write-off / Cr inventory. Same logic as
// stock adjustment, and therefore the same service: the handler only decides WHEN.
//
// An issue is a non-sale disposal, so the write-off account, not COGS:
// otherwise an internal transfer of goods would distort gross margin.
public partial class GoodsIssueGLEventHandler : TypedDocumentEventHandler<GoodsIssue>
{
    public override async Task<EventResult> OnAfterPostAsync(GoodsIssue document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Posted") return EventResult.Ok();

        var jeId = await context.GetService<IInventoryWriteOffGLService>()
            .PostAsync(document.MetaId, document.FromCell, document.DocumentDate,
                       "Goods issue " + document.MetaId);
        if (jeId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

        return EventResult.Ok();
    }
}
