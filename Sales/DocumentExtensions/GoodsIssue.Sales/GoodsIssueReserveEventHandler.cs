#nullable enable
using System;
using System.Collections.Generic;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Физический остаток отпуска проверяет владелец. Здесь — чужой резерв:
// расход не увозит товар, который уже обещан другому документу.
[ExtensionOf("GoodsIssue")]
public partial class GoodsIssueReserveEventHandler : TypedDocumentEventHandler<GoodsIssue>
{
    public override async Task<EventResult> OnBeforePostAsync(GoodsIssue header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<GoodsIssue>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;
        var need = new Dictionary<(Guid Cell, Guid Item), decimal>();
        foreach (var line in lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            if (qty <= 0m) continue;
            var cell = line.FromCell != Guid.Empty ? line.FromCell : (full?.FromCell ?? header.FromCell);
            var key = (cell, line.Item);
            need[key] = (need.TryGetValue(key, out var d) ? d : 0m) + qty;
        }

        var free = context.GetService<ICellFreeStock>();
        foreach (var kv in need)
        {
            var available = await free.FreeForDocumentAsync(header.MetaId, kv.Key.Cell, kv.Key.Item);
            if (kv.Value > available)
                return EventResult.Cancel(free.Shortage(kv.Value, available));
        }
        return EventResult.Ok();
    }
}
