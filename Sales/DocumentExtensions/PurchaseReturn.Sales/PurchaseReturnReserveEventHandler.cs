#nullable enable
using System.Collections.Generic;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Физический остаток проверяет владелец. Здесь — чужой резерв: возврат
// поставщику не увозит товар, который уже обещан другому документу.
[ExtensionOf("PurchaseReturn")]
public partial class PurchaseReturnReserveEventHandler : TypedDocumentEventHandler<PurchaseReturn>
{
    public override async Task<EventResult> OnBeforePostAsync(PurchaseReturn header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        if (header.Subtype != "Posted") return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<PurchaseReturn>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;
        var location = full?.Location ?? header.Location;
        var need = new Dictionary<Guid, decimal>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0m) continue;
            need[line.Item] = (need.TryGetValue(line.Item, out var d) ? d : 0m) + line.Quantity;
        }

        var free = context.GetService<ICellFreeStock>();
        foreach (var kv in need)
        {
            var available = await free.FreeForDocumentAsync(header.MetaId, location, kv.Key);
            if (kv.Value > available)
                return EventResult.Cancel(free.Shortage(kv.Value, available));
        }
        return EventResult.Ok();
    }
}
