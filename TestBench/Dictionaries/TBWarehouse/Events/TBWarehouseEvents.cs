#nullable enable
namespace ZuloOne.Runtime.Generated;

// «Ядерные тесты.Справочники»: детерминированный обработчик событий TBWarehouse.
// OnBeforeSave: имя "FORBIDDEN" отклоняется; имена в нижнем регистре переводятся
// в верхний (событие мутирует запись, и мутация сохраняется в БД).
//
// ВЛАДЕЛЕЦ цепочки: живёт в модели самого TBWarehouse, значит звено 0 — ниже
// некого, и next() он НЕ зовёт. Расширение из TestBenchExt стоит снаружи и
// всё равно исполняется: это и доказывает сценарий «цепочка по слоям».
public partial class TBWarehouseEventHandler : TypedDictionaryEventHandler<TBWarehouse>
{
    public override Task<EventResult> OnBeforeSaveAsync(TBWarehouse record, bool isNew, EventContext context){
        if (record.Name == "FORBIDDEN")
            return Task.FromResult(EventResult.Cancel("Name is forbidden"));
        if (!string.IsNullOrEmpty(record.Name) && record.Name.Any(char.IsLower))
            record.Name = record.Name.ToUpperInvariant();
        return Task.FromResult(EventResult.Ok());
    }
}