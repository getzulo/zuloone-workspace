#nullable enable
using System;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for TaxCategory records (MIQS DictionaryEventHandlerBase<T>).
// `record` is a typed TaxCategory entity — access fields directly (record.SomeField).
// Cancel with EventResult.Cancel("reason"); replace a DB error with EventResult.Error("...");
// show UI feedback with context.AddClientAction(ClientAction.Message("...", "success")).
//
// Категория обязана принадлежать налогу: именно по паре «налог + категория» код
// налога получает режим обложения, и категория без налога делает эту пару
// неполной — код с ней завести можно, а что она означает, не определено.
//
// ЧЕГО ЗДЕСЬ СОЗНАТЕЛЬНО НЕТ: проверки поля Treatment по списку допустимых
// значений (STANDARD / ZERO_RATED / EXEMPT / …). Это закрытый набор, и его место
// в МЕТАДАННЫХ — перечисление плюс EDT, как уже сделано для TaxRuleOperator в
// этой же модели. Белый список в обработчике выглядит проверкой, но новое
// значение придётся дописывать в код вместо справочника, а UI всё равно будет
// предлагать свободный ввод. Оставлено как есть до перевода поля в перечисление.
public partial class TaxCategoryEventHandler : TypedDictionaryEventHandler<TaxCategory>
{
    // Building a new record server-side: seed default field values here.
    public override Task<EventResult> OnBeforeCreateAsync(TaxCategory record, EventContext context)
    {
        // record.CreatedOn = DateTime.UtcNow;
        return Task.FromResult(EventResult.Ok());
    }

    // MIQS BeforeSave: runs before ANY save — insert (isNew == true) or update.
    // Put shared validation / computed fields here.
    public override Task<EventResult> OnBeforeSaveAsync(TaxCategory record, bool isNew, EventContext context)
    {
        if (record.Tax == Guid.Empty)
            return Task.FromResult(EventResult.Cancel("Укажите налог, к которому относится категория"));

        return Task.FromResult(EventResult.Ok());
    }

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(TaxCategory record, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(TaxCategory record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(TaxCategory record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(TaxCategory record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(TaxCategory record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before a record is deleted. Cancel to block the delete.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the record was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before inserting a clone: reset unique values (codes, numbers).
    public override Task<EventResult> OnBeforeCloneAsync(TaxCategory record, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After a record is loaded: compute transient/derived property values.
    public override Task<EventResult> OnAfterLoadAsync(TaxCategory record, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Validate a single field (name + current value).
    public override Task<EventResult> OnValidateFieldAsync(TaxCategory record, string fieldName, object? value, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(TaxCategory record, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // A delete failed: same friendly-message translation as OnSaveFailed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());
}
