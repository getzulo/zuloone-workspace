using System;
using ZuloOne.Services.Contracts;
#nullable enable
namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for TaxRule records (MIQS DictionaryEventHandlerBase<T>).
// `record` is a typed TaxRule entity — access fields directly (record.SomeField).
// Cancel with EventResult.Cancel("reason"); replace a DB error with EventResult.Error("...");
// show UI feedback with context.AddClientAction(ClientAction.Message("...", "success")).
public partial class TaxRuleEventHandler : TypedDictionaryEventHandler<TaxRule>
{
    // Building a new record server-side: seed default field values here.
    public override async Task<EventResult> OnBeforeCreateAsync(TaxRule record, EventContext context){
        var prior = await next(record, context);
        if (!prior.Success) return prior;

        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("TaxRule");
        if (record.EffectiveFrom.Year < 1902)
        {
            var createDay = createDefaults.PickDay(createSeed, "EffectiveFrom");
            if (createDay.Year >= 1902) record.EffectiveFrom = createDay;
        }
        // record.CreatedOn = DateTime.UtcNow;
        return EventResult.Ok();
    }

    // MIQS BeforeSave: runs before ANY save — insert (isNew == true) or update.
    // Put shared validation / computed fields here.
    public override async Task<EventResult> OnBeforeSaveAsync(TaxRule record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        // if (string.IsNullOrEmpty(record.Name))
        //     return EventResult.Cancel("Name is required");
        // context.AddClientAction(ClientAction.Message("Saved", "success"));
        return EventResult.Ok();
    }

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(TaxRule record, bool isNew, EventContext context)
        => next(record, isNew, context);

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(TaxRule record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(TaxRule record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(TaxRule record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(TaxRule record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before a record is deleted. Cancel to block the delete.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // After the record was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // Before inserting a clone: reset unique values (codes, numbers).
    public override Task<EventResult> OnBeforeCloneAsync(TaxRule record, EventContext context)
        => next(record, context);

    // After a record is loaded: compute transient/derived property values.
    public override Task<EventResult> OnAfterLoadAsync(TaxRule record, EventContext context)
        => next(record, context);

    // Validate a single field (name + current value).
    public override Task<EventResult> OnValidateFieldAsync(TaxRule record, string fieldName, object? value, EventContext context)
        => next(record, fieldName, value, context);

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(TaxRule record, string errorMessage, EventContext context)
        => next(record, errorMessage, context);

    // A delete failed: same friendly-message translation as OnSaveFailed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => next(recordId, errorMessage, context);
}
