#nullable enable
namespace ZuloOne.Runtime.Generated;

// "TestBench.Workspace" (VSC-8a): TBWarehouse chain extension link from
// the TestBenchExt model (layer 2). Runs AFTER the base: sees the name already
// in uppercase and appends a lowercase suffix — order is provable by case.
public partial class TBWarehouseEventHandler : TypedDictionaryEventHandler<TBWarehouse>
{
    public override async Task<EventResult> OnBeforeSaveAsync(TBWarehouse record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        // Inner link writes the bag; hydrate Name so work after next() sees
        // the owner's uppercase (typed next() does this on a rebuilt Runtime).
        if (context.Entity is IDictionary<string, object?> bag
            && bag.TryGetValue("Name", out var raw)
            && raw is string stored)
            record.Name = stored;

        if (!string.IsNullOrEmpty(record.Name)
            && record.Name.StartsWith("CHAIN", StringComparison.OrdinalIgnoreCase)
            && context.PreviousResult?.Success == true
            && !record.Name.EndsWith("-ext", StringComparison.Ordinal))
        {
            record.Name += "-ext";
        }
        return EventResult.Ok();
    }
}