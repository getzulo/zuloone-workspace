#nullable enable
using System;
using System.Collections.Generic;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Приход корректировки резерв не трогает. Списание — да: минус в строке
// не должен съесть чужой ReservedStock.
[ExtensionOf("StockAdjustment")]
public partial class StockAdjustmentReserveEventHandler : TypedDocumentEventHandler<StockAdjustment>
{
    public override async Task<EventResult> OnBeforePostAsync(StockAdjustment header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<StockAdjustment>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;
        var need = new Dictionary<(Guid Cell, Guid Item), decimal>();
        foreach (var line in lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            if (qty >= 0m) continue;
            var cell = line.Cell != Guid.Empty ? line.Cell : (full?.Cell ?? header.Cell);
            var key = (cell, line.Item);
            need[key] = (need.TryGetValue(key, out var d) ? d : 0m) + (-qty);
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
