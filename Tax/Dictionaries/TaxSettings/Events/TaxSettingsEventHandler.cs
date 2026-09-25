#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for TaxSettings records (MIQS DictionaryEventHandlerBase<T>).
// `record` is a typed TaxSettings entity — access fields directly (record.SomeField).
// Cancel with EventResult.Cancel("reason"); replace a DB error with EventResult.Error("...");
// show UI feedback with context.AddClientAction(ClientAction.Message("...", "success")).
public partial class TaxSettingsEventHandler : TypedDictionaryEventHandler<TaxSettings>
{
    // Building a new record server-side: seed default field values here.
    public override async Task<EventResult> OnBeforeCreateAsync(TaxSettings record, EventContext context){
        var prior = await next(record, context);
        if (!prior.Success) return prior;

        // record.CreatedOn = DateTime.UtcNow;
        return EventResult.Ok();
    }

    // MIQS BeforeSave: runs before ANY save — insert (isNew == true) or update.
    // Put shared validation / computed fields here.
    public override async Task<EventResult> OnBeforeSaveAsync(TaxSettings record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        // Движок читает DefaultTaxCode. Ссылка штампует код, когда её сменили.
        // Код, который сменили при той же ссылке, остаётся: OnAfterLoad уже
        // подставил ссылку по старому коду, и штамп затёр бы новую строку.
        var codes = context.GetService<IDictionaryManager<TaxCode>>();
        TaxSettings? stored = null;
        if (!isNew && record.MetaId != Guid.Empty)
            stored = await context.GetService<IDictionaryManager<TaxSettings>>().GetRecordAsync(record.MetaId);
        await AlignTaxCodeAsync(codes, stored?.DefaultTax ?? Guid.Empty, stored?.DefaultTaxCode,
            record.DefaultTax, record.DefaultTaxCode,
            id => record.DefaultTax = id, code => record.DefaultTaxCode = code);

        return EventResult.Ok();
    }

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(TaxSettings record, bool isNew, EventContext context)
        => next(record, isNew, context);

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(TaxSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(TaxSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(TaxSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(TaxSettings record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before a record is deleted. Cancel to block the delete.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // After the record was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // Before inserting a clone: reset unique values (codes, numbers).
    public override Task<EventResult> OnBeforeCloneAsync(TaxSettings record, EventContext context)
        => next(record, context);

    // After a record is loaded: compute transient/derived property values.
    public override async Task<EventResult> OnAfterLoadAsync(TaxSettings record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        if (record.DefaultTax == Guid.Empty && !string.IsNullOrWhiteSpace(record.DefaultTaxCode))
        {
            var code = record.DefaultTaxCode.Replace("'", "''");
            var row = (await context.GetService<IDictionaryManager<TaxCode>>()
                .GetRecordsAsync($"Code = '{code}'", take: 1)).FirstOrDefault();
            if (row != null) record.DefaultTax = row.MetaId;
        }
        return EventResult.Ok();
    }

    // Validate a single field (name + current value).
    public override Task<EventResult> OnValidateFieldAsync(TaxSettings record, string fieldName, object? value, EventContext context)
        => next(record, fieldName, value, context);

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(TaxSettings record, string errorMessage, EventContext context)
        => next(record, errorMessage, context);

    // A delete failed: same friendly-message translation as OnSaveFailed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => next(recordId, errorMessage, context);

    private static async Task AlignTaxCodeAsync(
        IDictionaryManager<TaxCode> codes,
        Guid storedId, string? storedCode, Guid currentId, string? currentCode,
        Action<Guid> setId, Action<string> setCode)
    {
        var refChanged = currentId != storedId;
        var codeChanged = (currentCode ?? "") != (storedCode ?? "");
        if (refChanged && currentId != Guid.Empty)
        {
            var stamped = await CodeOfTaxAsync(codes, currentId, currentCode);
            if (!string.IsNullOrWhiteSpace(stamped)) setCode(stamped!);
            return;
        }
        if (codeChanged && currentId != Guid.Empty)
        {
            var refCode = await CodeOfTaxAsync(codes, currentId, null);
            if ((refCode ?? "") != (currentCode ?? ""))
                setId(await IdOfTaxAsync(codes, currentCode));
            return;
        }
        if (currentId != Guid.Empty && string.IsNullOrWhiteSpace(currentCode))
        {
            var stamped = await CodeOfTaxAsync(codes, currentId, currentCode);
            if (!string.IsNullOrWhiteSpace(stamped)) setCode(stamped!);
        }
    }

    private static async Task<string?> CodeOfTaxAsync(
        IDictionaryManager<TaxCode> codes, Guid id, string? fallback)
    {
        var row = await codes.GetRecordAsync(id);
        return string.IsNullOrWhiteSpace(row?.Code) ? fallback : row!.Code;
    }

    private static async Task<Guid> IdOfTaxAsync(IDictionaryManager<TaxCode> codes, string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return Guid.Empty;
        var lit = code.Replace("'", "''");
        var row = (await codes.GetRecordsAsync($"Code = '{lit}'", take: 1)).FirstOrDefault();
        return row?.MetaId ?? Guid.Empty;
    }
}
