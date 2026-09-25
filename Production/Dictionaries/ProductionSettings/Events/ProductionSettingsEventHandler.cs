#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for ProductionSettings records (MIQS DictionaryEventHandlerBase<T>).
// `record` is a typed ProductionSettings entity — access fields directly (record.SomeField).
// Cancel with EventResult.Cancel("reason"); replace a DB error with EventResult.Error("...");
// show UI feedback with context.AddClientAction(ClientAction.Message("...", "success")).
public partial class ProductionSettingsEventHandler : TypedDictionaryEventHandler<ProductionSettings>
{
    // Building a new record server-side: seed default field values here.
    public override async Task<EventResult> OnBeforeCreateAsync(ProductionSettings record, EventContext context){
        var prior = await next(record, context);
        if (!prior.Success) return prior;

        // record.CreatedOn = DateTime.UtcNow;
        return EventResult.Ok();
    }

    // MIQS BeforeSave: runs before ANY save — insert (isNew == true) or update.
    // Put shared validation / computed fields here.
    public override async Task<EventResult> OnBeforeSaveAsync(ProductionSettings record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.DefaultOutputLocation != Guid.Empty)
        {
            var row = await context.GetService<IDictionaryManager<StoreCell>>()
                .GetRecordAsync(record.DefaultOutputLocation);
            if (!string.IsNullOrWhiteSpace(row?.ID))
                record.DefaultOutputLocationCode = row!.ID;
        }

        return EventResult.Ok();
    }

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(ProductionSettings record, bool isNew, EventContext context)
        => next(record, isNew, context);

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(ProductionSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(ProductionSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(ProductionSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(ProductionSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before a record is deleted. Cancel to block the delete.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // After the record was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // Before inserting a clone: reset unique values (codes, numbers).
    public override Task<EventResult> OnBeforeCloneAsync(ProductionSettings record, EventContext context)
        => next(record, context);

    // After a record is loaded: compute transient/derived property values.
    public override async Task<EventResult> OnAfterLoadAsync(ProductionSettings record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        if (record.DefaultOutputLocation == Guid.Empty && !string.IsNullOrWhiteSpace(record.DefaultOutputLocationCode))
        {
            var id = record.DefaultOutputLocationCode.Replace("'", "''");
            var row = (await context.GetService<IDictionaryManager<StoreCell>>()
                .GetRecordsAsync($"ID = '{id}'", take: 1)).FirstOrDefault();
            if (row != null) record.DefaultOutputLocation = row.MetaId;
        }
        return EventResult.Ok();
    }

    // Validate a single field (name + current value).
    public override Task<EventResult> OnValidateFieldAsync(ProductionSettings record, string fieldName, object? value, EventContext context)
        => next(record, fieldName, value, context);

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(ProductionSettings record, string errorMessage, EventContext context)
        => next(record, errorMessage, context);

    // A delete failed: same friendly-message translation as OnSaveFailed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => next(recordId, errorMessage, context);
}
