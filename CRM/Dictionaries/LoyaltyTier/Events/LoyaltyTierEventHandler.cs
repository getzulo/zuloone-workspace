#nullable enable
namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for LoyaltyTier records (MIQS DictionaryEventHandlerBase<T>).
// `record` is a typed LoyaltyTier entity — access fields directly (record.SomeField).
// Cancel with EventResult.Cancel("reason"); replace a DB error with EventResult.Error("...");
// show UI feedback with context.AddClientAction(ClientAction.Message("...", "success")).
public partial class LoyaltyTierEventHandler : TypedDictionaryEventHandler<LoyaltyTier>
{
    // Building a new record server-side: seed default field values here.
    public override async Task<EventResult> OnBeforeCreateAsync(LoyaltyTier record, EventContext context){
        var prior = await next(record, context);
        if (!prior.Success) return prior;

        // record.CreatedOn = DateTime.UtcNow;
        return EventResult.Ok();
    }

    // MIQS BeforeSave: runs before ANY save — insert (isNew == true) or update.
    // DiscountPercent is stamped onto SalesRealization and from there hits ALL monetary
    // legs through PricingService.LineAmount — outside [0, 100] it either means
    // nothing (negative is a markup, not a discount) or flips the sign of the
    // line amount (>100%), and that must be rejected here, not on the invoice.
    public override async Task<EventResult> OnBeforeSaveAsync(LoyaltyTier record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.DiscountPercent < 0m || record.DiscountPercent > 100m)
            return EventResult.Cancel("Скидка уровня должна быть в диапазоне от 0 до 100%");
        if (record.EarnRate < 0m)
            return EventResult.Cancel("Курс начисления уровня не может быть отрицательным.");
        return EventResult.Ok();
    }

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(LoyaltyTier record, bool isNew, EventContext context)
        => next(record, isNew, context);

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(LoyaltyTier record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(LoyaltyTier record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(LoyaltyTier record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(LoyaltyTier record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before a record is deleted. Cancel to block the delete.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // After the record was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // Before inserting a clone: reset unique values (codes, numbers).
    public override Task<EventResult> OnBeforeCloneAsync(LoyaltyTier record, EventContext context)
        => next(record, context);

    // After a record is loaded: compute transient/derived property values.
    public override Task<EventResult> OnAfterLoadAsync(LoyaltyTier record, EventContext context)
        => next(record, context);

    // Validate a single field (name + current value).
    public override Task<EventResult> OnValidateFieldAsync(LoyaltyTier record, string fieldName, object? value, EventContext context)
        => next(record, fieldName, value, context);

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(LoyaltyTier record, string errorMessage, EventContext context)
        => next(record, errorMessage, context);

    // A delete failed: same friendly-message translation as OnSaveFailed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => next(recordId, errorMessage, context);
}
