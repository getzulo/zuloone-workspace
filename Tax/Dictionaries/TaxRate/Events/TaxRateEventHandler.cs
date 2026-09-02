#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for TaxRate records (MIQS DictionaryEventHandlerBase<T>).
// `record` is a typed TaxRate entity — access fields directly (record.SomeField).
// Cancel with EventResult.Cancel("reason"); replace a DB error with EventResult.Error("...");
// show UI feedback with context.AddClientAction(ClientAction.Message("...", "success")).
//
// ═══ НЕПЕРЕСЕКАЮЩАЯСЯ ИСТОРИЯ СТАВОК ════════════════════════════════════════
//
// Ставка налога — величина, действующая в ОКНЕ дат: «НДС 15% с 01.07.2020». На
// любую дату у налога обязана действовать ровно одна ставка. TaxService это
// требование знает и при двух подходящих ставках БРОСАЕТ исключение — но узнаёт
// об этом в момент выпуска счёта, то есть ошибка мастер-данных останавливает
// операционную работу и всплывает не там, где её допустили.
//
// Проверка перенесена на ВВОД: пересечение окон отклоняется в момент заведения
// ставки, когда человек как раз занят справочником и видит соседние строки.
// Отказ в TaxService остаётся как последний рубеж — на случай данных, залитых
// в обход событий (импорт, миграция, прямой SQL).
//
// САМО ПРАВИЛО ЗДЕСЬ НЕ ЖИВЁТ: обработчик спрашивает TaxService, у которого оно
// одно на обе двери (FindOverlappingRateAsync). Иначе граница окна могла бы
// разъехаться между «что нельзя завести» и «на чём падает расчёт».
public partial class TaxRateEventHandler : TypedDictionaryEventHandler<TaxRate>
{
    // Building a new record server-side: seed default field values here.
    public override Task<EventResult> OnBeforeCreateAsync(TaxRate record, EventContext context)
    {
        // record.CreatedOn = DateTime.UtcNow;
        return Task.FromResult(EventResult.Ok());
    }

    // MIQS BeforeSave: runs before ANY save — insert (isNew == true) or update.
    // Put shared validation / computed fields here.
    public override async Task<EventResult> OnBeforeSaveAsync(TaxRate record, bool isNew, EventContext context)
    {
        if (record.Tax == Guid.Empty)
            return EventResult.Cancel("Укажите налог, ставкой которого является запись");

        if (record.Rate < 0m)
            return EventResult.Cancel("Ставка не может быть отрицательной");

        if (record.EffectiveTo.HasValue && record.EffectiveFrom > record.EffectiveTo.Value)
            return EventResult.Cancel("Окно действия задано наоборот: дата начала позже даты окончания");

        var clash = await context.GetService<ITaxService>()
            .FindOverlappingRateAsync(record.Tax, record.MetaId, record.EffectiveFrom, record.EffectiveTo);
        if (clash != null)
            return EventResult.Cancel(
                $"Окно действия пересекается со ставкой «{clash.Code}» ({clash.Rate}): "
                + $"{Window(clash.EffectiveFrom, clash.EffectiveTo)}. "
                + "У налога на каждую дату должна действовать ровно одна ставка — "
                + "закройте предыдущую ставку датой окончания.");

        return EventResult.Ok();
    }

    private static string Window(DateTime from, DateTime? to)
        => to.HasValue ? $"{from:yyyy-MM-dd} — {to.Value:yyyy-MM-dd}" : $"с {from:yyyy-MM-dd}";

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(TaxRate record, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(TaxRate record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(TaxRate record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(TaxRate record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(TaxRate record, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before a record is deleted. Cancel to block the delete.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the record was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before inserting a clone: reset unique values (codes, numbers).
    public override Task<EventResult> OnBeforeCloneAsync(TaxRate record, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After a record is loaded: compute transient/derived property values.
    public override Task<EventResult> OnAfterLoadAsync(TaxRate record, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Validate a single field (name + current value).
    public override Task<EventResult> OnValidateFieldAsync(TaxRate record, string fieldName, object? value, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(TaxRate record, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // A delete failed: same friendly-message translation as OnSaveFailed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());
}
