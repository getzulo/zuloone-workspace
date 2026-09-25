#nullable enable
using System;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for CostingSettings records (MIQS DictionaryEventHandlerBase<T>).
// `record` is a typed CostingSettings entity — access fields directly (record.SomeField).
// Cancel with EventResult.Cancel("reason"); replace a DB error with EventResult.Error("...");
// show UI feedback with context.AddClientAction(ClientAction.Message("...", "success")).
public partial class CostingSettingsEventHandler : TypedDictionaryEventHandler<CostingSettings>
{
    // Building a new record server-side: seed default field values here.
    public override async Task<EventResult> OnBeforeCreateAsync(CostingSettings record, EventContext context){
        var prior = await next(record, context);
        if (!prior.Success) return prior;

        // record.CreatedOn = DateTime.UtcNow;
        return EventResult.Ok();
    }

    // MIQS BeforeSave: runs before ANY save — insert (isNew == true) or update.
    // Put shared validation / computed fields here.
    public override async Task<EventResult> OnBeforeSaveAsync(CostingSettings record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        // Драйвер читает строку. «Не задано» её не трогает: на UPDATE ноль
        // значит «поле не пришло», и тест, который пишет FIFO строкой, жив.
        if (record.ValuationMethod == CostingMethodKind.Fifo)
            record.CostingMethod = "FIFO";
        else if (record.ValuationMethod == CostingMethodKind.Average)
            record.CostingMethod = "AVG";

        return EventResult.Ok();
    }

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(CostingSettings record, bool isNew, EventContext context)
        => next(record, isNew, context);

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(CostingSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(CostingSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(CostingSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(CostingSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before a record is deleted. Cancel to block the delete.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // After the record was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // Before inserting a clone: reset unique values (codes, numbers).
    public override Task<EventResult> OnBeforeCloneAsync(CostingSettings record, EventContext context)
        => next(record, context);

    // After a record is loaded: compute transient/derived property values.
    public override async Task<EventResult> OnAfterLoadAsync(CostingSettings record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        if (record.ValuationMethod == CostingMethodKind.Unspecified)
        {
            if (string.Equals(record.CostingMethod, "AVG", StringComparison.OrdinalIgnoreCase))
                record.ValuationMethod = CostingMethodKind.Average;
            else if (string.Equals(record.CostingMethod, "FIFO", StringComparison.OrdinalIgnoreCase))
                record.ValuationMethod = CostingMethodKind.Fifo;
        }
        return EventResult.Ok();
    }

    // Validate a single field (name + current value).
    public override Task<EventResult> OnValidateFieldAsync(CostingSettings record, string fieldName, object? value, EventContext context)
        => next(record, fieldName, value, context);

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(CostingSettings record, string errorMessage, EventContext context)
        => next(record, errorMessage, context);

    // A delete failed: same friendly-message translation as OnSaveFailed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => next(recordId, errorMessage, context);
}
