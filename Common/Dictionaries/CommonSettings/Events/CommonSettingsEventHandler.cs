#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for CommonSettings records (MIQS DictionaryEventHandlerBase<T>).
// `record` is a typed CommonSettings entity — access fields directly (record.SomeField).
// Cancel with EventResult.Cancel("reason"); replace a DB error with EventResult.Error("...");
// show UI feedback with context.AddClientAction(ClientAction.Message("...", "success")).
public partial class CommonSettingsEventHandler : TypedDictionaryEventHandler<CommonSettings>
{
    // Building a new record server-side: seed default field values here.
    public override async Task<EventResult> OnBeforeCreateAsync(CommonSettings record, EventContext context){
        var prior = await next(record, context);
        if (!prior.Success) return prior;

        // record.CreatedOn = DateTime.UtcNow;
        return EventResult.Ok();
    }

    // MIQS BeforeSave: runs before ANY save — insert (isNew == true) or update.
    // Put shared validation / computed fields here.
    public override async Task<EventResult> OnBeforeSaveAsync(CommonSettings record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        // Ссылка — то, что видит форма. Статус тенанта и старые строки читают
        // код. Пустую ссылку не трогаем: UPDATE гидратирует только затронутые
        // поля, и «пусто» здесь значит «поле не пришло», а не «стереть UAH».
        if (record.DefaultCurrency != Guid.Empty)
        {
            var currencies = context.GetService<IDictionaryManager<Currency>>();
            var row = await currencies.GetRecordAsync(record.DefaultCurrency);
            if (!string.IsNullOrWhiteSpace(row?.Code))
                record.DefaultCurrencyCode = row!.Code;
        }

        return EventResult.Ok();
    }

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(CommonSettings record, bool isNew, EventContext context)
        => next(record, isNew, context);

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(CommonSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(CommonSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(CommonSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(CommonSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before a record is deleted. Cancel to block the delete.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // After the record was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // Before inserting a clone: reset unique values (codes, numbers).
    public override Task<EventResult> OnBeforeCloneAsync(CommonSettings record, EventContext context)
        => next(record, context);

    // After a record is loaded: compute transient/derived property values.
    public override async Task<EventResult> OnAfterLoadAsync(CommonSettings record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;

        if (record.DefaultCurrency == Guid.Empty && !string.IsNullOrWhiteSpace(record.DefaultCurrencyCode))
        {
            var code = record.DefaultCurrencyCode.Replace("'", "''");
            var currencies = context.GetService<IDictionaryManager<Currency>>();
            var row = (await currencies.GetRecordsAsync($"Code = '{code}'", take: 1)).FirstOrDefault();
            if (row != null) record.DefaultCurrency = row.MetaId;
        }

        return EventResult.Ok();
    }

    // Validate a single field (name + current value).
    public override Task<EventResult> OnValidateFieldAsync(CommonSettings record, string fieldName, object? value, EventContext context)
        => next(record, fieldName, value, context);

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(CommonSettings record, string errorMessage, EventContext context)
        => next(record, errorMessage, context);

    // A delete failed: same friendly-message translation as OnSaveFailed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => next(recordId, errorMessage, context);
}
