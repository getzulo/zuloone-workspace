#nullable enable
namespace ZuloOne.Runtime.Generated;

// Handler for TBItem. Override only the hooks you need.
// Chain of Command: the rightmost (highest-layer) override runs first;
// next(...) continues to the lower link and returns its result.
// Work AFTER next() sees what they wrote. Forgetting next() is ZOCOC001.
// [Replace] swallows the chain on purpose.
public partial class TBItemEventHandler : TypedDictionaryEventHandler<TBItem>
{

    public override async Task<EventResult> OnAfterSaveAsync(TBItem record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnAfterLoadAsync(TBItem record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        return EventResult.Ok();
    }
}
