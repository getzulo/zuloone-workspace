#nullable enable
namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for Position records (MIQS DictionaryEventHandlerBase<T>).
// `record` is a typed Position entity — access fields directly (record.SomeField).
// Cancel with EventResult.Cancel("reason"); replace a DB error with EventResult.Error("...");
// show UI feedback with context.AddClientAction(ClientAction.Message("...", "success")).
public partial class PositionEventHandler : TypedDictionaryEventHandler<Position>
{
    // Building a new record server-side: seed default field values here.
    public override Task<EventResult> OnBeforeCreateAsync(Position record, EventContext context)
    {
        // record.CreatedOn = DateTime.UtcNow;
        return Task.FromResult(EventResult.Ok());
    }

    // MIQS BeforeSave: runs before ANY save — insert (isNew == true) or update.
    // Put shared validation / computed fields here.
    public override Task<EventResult> OnBeforeSaveAsync(Position record, bool isNew, EventContext context)
    {
        // if (string.IsNullOrEmpty(record.Name))
        //     return Task.FromResult(EventResult.Cancel("Name is required"));
        // context.AddClientAction(ClientAction.Message("Saved", "success"));
        return Task.FromResult(EventResult.Ok());
    }

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(Position record, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(Position record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(Position record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(Position record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(Position record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before a record is deleted. Cancel to block the delete.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the record was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before inserting a clone: reset unique values (codes, numbers).
    public override Task<EventResult> OnBeforeCloneAsync(Position record, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After a record is loaded: compute transient/derived property values.
    public override Task<EventResult> OnAfterLoadAsync(Position record, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Validate a single field (name + current value).
    public override Task<EventResult> OnValidateFieldAsync(Position record, string fieldName, object? value, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(Position record, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // A delete failed: same friendly-message translation as OnSaveFailed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());
}
