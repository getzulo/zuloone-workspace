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
// ═══ ЦЕЛОСТНОСТЬ НАЛОГОВОГО КОДА ════════════════════════════════════════════
//
// Код ссылается на налог ТРЕМЯ путями: напрямую (Tax), через категорию
// (TaxCategory.Tax) и через ставку (TaxRate.Tax). Платформа следит лишь за тем,
// что ссылки ведут на существующие записи, но не за тем, что они ведут к ОДНОМУ
// И ТОМУ ЖЕ налогу. Без проверки заводится код, у которого налог — НДС, а
// категория принадлежит налогу на прибыль; расчёт возьмёт ставку по Tax и
// применит режим обложения чужого налога, а заметить это можно только по
// расхождению в декларации.
//
// Место проверки выбрано осознанно — на ВВОДЕ, а не при проведении. Ошибка в
// мастер-данных, всплывающая при выпуске счёта, останавливает работу в момент,
// когда человек занят другим, и чинить её приходится в другом месте системы.
public partial class TaxCodeEventHandler : TypedDictionaryEventHandler<TaxCode>
{
    // Building a new record server-side: seed default field values here.
    public override Task<EventResult> OnBeforeCreateAsync(TaxCode record, EventContext context)
    {
        // record.CreatedOn = DateTime.UtcNow;
        return Task.FromResult(EventResult.Ok());
    }

    // MIQS BeforeSave: runs before ANY save — insert (isNew == true) or update.
    // Put shared validation / computed fields here.
    public override async Task<EventResult> OnBeforeSaveAsync(TaxCode record, bool isNew, EventContext context)
    {
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

        // Ставка у кода — ИСТОРИЧЕСКАЯ привязка, расчёт её не читает: действующая
        // ставка подбирается по налогу и дате (см. TaxService). Но если она
        // заполнена, то обязана быть непротиворечивой — ставка чужого налога в
        // коде это ложный след для того, кто будет разбираться в расчёте.
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
        => Task.FromResult(EventResult.Ok());

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
        => Task.FromResult(EventResult.Ok());

    // After the record was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before inserting a clone: reset unique values (codes, numbers).
    public override Task<EventResult> OnBeforeCloneAsync(TaxCode record, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After a record is loaded: compute transient/derived property values.
    public override Task<EventResult> OnAfterLoadAsync(TaxCode record, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Validate a single field (name + current value).
    public override Task<EventResult> OnValidateFieldAsync(TaxCode record, string fieldName, object? value, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(TaxCode record, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // A delete failed: same friendly-message translation as OnSaveFailed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());
}
