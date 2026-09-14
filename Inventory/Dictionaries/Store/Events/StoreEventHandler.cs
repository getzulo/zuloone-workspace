#nullable enable
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// A new store with discipline already on immediately gets a yard
// (receiving / storage / picking). With the flag off it creates nothing: tests
// and old stores draw their own cells, and the first Storage must stay theirs, not ours.
public partial class StoreEventHandler : TypedDictionaryEventHandler<Store>
{
    public override async Task<EventResult> OnAfterSaveAsync(Store record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        var cells = context.GetService<IStoreCellService>();
        if (await cells.IsWarehouseDisciplineOnAsync())
            await cells.EnsureYardAsync(record.MetaId);
        return EventResult.Ok();
    }
}
