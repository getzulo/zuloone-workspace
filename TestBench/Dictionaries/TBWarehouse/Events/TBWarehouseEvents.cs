#nullable enable
namespace ZuloOne.Runtime.Generated;

// «Ядерные тесты.Справочники»: детерминированный обработчик событий TBWarehouse.
// OnBeforeSave: имя "FORBIDDEN" отклоняется; имена в нижнем регистре переводятся
// в верхний (событие мутирует запись, и мутация сохраняется в БД).
public partial class TBWarehouseEventHandler : TypedDictionaryEventHandler<TBWarehouse>
{
    public override Task<EventResult> OnBeforeSaveAsync(TBWarehouse record, bool isNew, EventContext context)
    {
        if (record.Name == "FORBIDDEN")
            return Task.FromResult(EventResult.Cancel("Name is forbidden"));
        if (!string.IsNullOrEmpty(record.Name) && record.Name.Any(char.IsLower))
            record.Name = record.Name.ToUpperInvariant();
        return Task.FromResult(EventResult.Ok());
    }
}