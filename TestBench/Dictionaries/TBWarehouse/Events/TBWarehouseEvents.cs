#nullable enable
namespace ZuloOne.Runtime.Generated;

// "TestBench.Dictionaries": deterministic TBWarehouse event handler.
// OnBeforeSave: the name "FORBIDDEN" is rejected; lowercase names are converted
// to uppercase (the event mutates the record, and the mutation is persisted to the DB).
public partial class TBWarehouseEventHandler : TypedDictionaryEventHandler<TBWarehouse>
{
    public override async Task<EventResult> OnBeforeSaveAsync(TBWarehouse record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.Name == "FORBIDDEN")
            return EventResult.Cancel("Name is forbidden");
        if (!string.IsNullOrEmpty(record.Name) && record.Name.Any(char.IsLower))
            record.Name = record.Name.ToUpperInvariant();
        return EventResult.Ok();
    }
}