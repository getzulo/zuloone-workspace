#nullable enable
using System;
using System.Collections.Generic;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

[ExtensionOf("PutAwayTask")]
public partial class PutAwayReserveEventHandler : TypedDocumentEventHandler<PutAwayTask>
{
    public override async Task<EventResult> OnBeforePostAsync(PutAwayTask header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        if (header.Subtype != "Confirmed") return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<PutAwayTask>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;
        var fromCell = full?.FromCell ?? header.FromCell;
        var need = new Dictionary<Guid, decimal>();
        foreach (var line in lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            if (qty <= 0m) continue;
            need[line.Item] = (need.TryGetValue(line.Item, out var d) ? d : 0m) + qty;
        }

        var free = context.GetService<ICellFreeStock>();
        foreach (var kv in need)
        {
            var available = await free.FreeForDocumentAsync(header.MetaId, fromCell, kv.Key);
            if (kv.Value > available)
                return EventResult.Cancel(free.Shortage(kv.Value, available));
        }
        return EventResult.Ok();
    }
}
