#nullable enable
namespace ZuloOne.Runtime.Generated;

// «Ядерные тесты.Справочники»: детерминированный обработчик событий TBWarehouse.
// OnBeforeSave: имя "FORBIDDEN" отклоняется; имена в нижнем регистре переводятся
// в верхний (событие мутирует запись, и мутация сохраняется в БД).
public partial class TBWarehouseEventHandler : TypedDictionaryEventHandler<TBWarehouse>
{
    public override async Task<EventResult> OnBeforeSaveAsync(TBWarehouse record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.Name == "FORBIDDEN")
            return EventResult.Cancel("Name is forbidden");
        if (!string.IsNullOrEmpty(record.Name) && record.Name.Any(char.IsLower))
            record.Name = record.Name.ToUpperInvariant();
        return EventResult.Ok();
    }
}