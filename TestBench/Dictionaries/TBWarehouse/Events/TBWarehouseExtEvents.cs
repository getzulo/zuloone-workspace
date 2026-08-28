#nullable enable
namespace ZuloOne.Runtime.Generated;

// «Ядерные тесты.Воркспейс» (VSC-8a): звено-расширение цепочки TBWarehouse из
// модели TestBenchExt (слой 2). Выполняется ПОСЛЕ базового: видит имя уже в
// верхнем регистре и дописывает суффикс в нижнем — порядок доказуем по регистру.
public partial class TBWarehouseEventHandler : TypedDictionaryEventHandler<TBWarehouse>
{
    public override Task<EventResult> OnBeforeSaveAsync(TBWarehouse record, bool isNew, EventContext context)
    {
        if (!string.IsNullOrEmpty(record.Name)
            && record.Name.StartsWith("CHAIN", StringComparison.OrdinalIgnoreCase)
            && context.PreviousResult?.Success == true
            && !record.Name.EndsWith("-ext", StringComparison.Ordinal))
        {
            record.Name += "-ext";
        }
        return Task.FromResult(EventResult.Ok());
    }
}