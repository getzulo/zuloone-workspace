#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for TaxCode records (MIQS DictionaryEventHandlerBase<T>).
// `record` is a typed TaxCode entity — access fields directly (record.SomeField).
// Cancel with EventResult.Cancel("reason"); replace a DB error with EventResult.Error("...");
// show UI feedback with context.AddClientAction(ClientAction.Message("...", "success")).
//
// ═══ TAX CODE INTEGRITY ═════════════════════════════════════════════════════
//
// A code references a tax THREE ways: directly (Tax), through the category
// (TaxCategory.Tax) and through the rate (TaxRate.Tax). The platform only
// watches that the links point at existing records, not that they point at ONE
// AND THE SAME tax. Without the check a code can be created whose tax is VAT
// while the category belongs to income tax; the calculation would take the rate
// from Tax and apply another tax's treatment, and that would only show up as a
// discrepancy on the return.
//
// The check is placed on INPUT, not on posting, on purpose. A master-data error
// that surfaces when an invoice is issued stops work at a moment when the person
// is busy with something else, and the fix has to be made elsewhere in the
// system.
public partial class TaxCodeEventHandler : TypedDictionaryEventHandler<TaxCode>
{
    // Building a new record server-side: seed default field values here.
    public override async Task<EventResult> OnBeforeCreateAsync(TaxCode record, EventContext context){
        var prior = await next(record, context);
        if (!prior.Success) return prior;

        // record.CreatedOn = DateTime.UtcNow;
        return EventResult.Ok();
    }

    // MIQS BeforeSave: runs before ANY save — insert (isNew == true) or update.
    // Put shared validation / computed fields here.
    public override async Task<EventResult> OnBeforeSaveAsync(TaxCode record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.Tax == Guid.Empty)
            return EventResult.Cancel("Укажите налог, к которому относится код");

        if (record.EffectiveTo.HasValue && record.EffectiveFrom > record.EffectiveTo.Value)
            return EventResult.Cancel("Окно действия задано наоборот: дата начала позже даты окончания");

        if (record.TaxCategory != Guid.Empty)
        {
            var category = await context.GetService<IDictionaryManager<TaxCategory>>()
                .GetRecordAsync(record.TaxCategory);
            if (category != null && category.Tax != Guid.Empty && category.Tax != record.Tax)
                return EventResult.Cancel(
                    $"Категория «{category.Name}» принадлежит другому налогу: "
                    + "код и его категория обязаны относиться к одному налогу");
        }

        // The rate on the code is a HISTORICAL binding; the calculation does not
        // read it: the effective rate is resolved by tax and date (see TaxService).
        // But if it is filled, it must be consistent — another tax's rate on the
        // code is a false trail for whoever later traces the calculation.
        if (record.TaxRate != Guid.Empty)
        {
            var rate = await context.GetService<IDictionaryManager<TaxRate>>()
                .GetRecordAsync(record.TaxRate);
            if (rate != null && rate.Tax != Guid.Empty && rate.Tax != record.Tax)
                return EventResult.Cancel(
                    $"Ставка «{rate.Code}» принадлежит другому налогу: "
                    + "код и его ставка обязаны относиться к одному налогу");
        }

        return EventResult.Ok();
    }

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(TaxCode record, bool isNew, EventContext context)
        => next(record, isNew, context);

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(TaxCode record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(TaxCode record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(TaxCode record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(TaxCode record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before a record is deleted. Cancel to block the delete.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // After the record was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // Before inserting a clone: reset unique values (codes, numbers).
    public override Task<EventResult> OnBeforeCloneAsync(TaxCode record, EventContext context)
        => next(record, context);

    // After a record is loaded: compute transient/derived property values.
    public override Task<EventResult> OnAfterLoadAsync(TaxCode record, EventContext context)
        => next(record, context);

    // Validate a single field (name + current value).
    public override Task<EventResult> OnValidateFieldAsync(TaxCode record, string fieldName, object? value, EventContext context)
        => next(record, fieldName, value, context);

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(TaxCode record, string errorMessage, EventContext context)
        => next(record, errorMessage, context);

    // A delete failed: same friendly-message translation as OnSaveFailed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => next(recordId, errorMessage, context);
}
