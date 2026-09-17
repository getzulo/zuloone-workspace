#nullable enable
namespace ZuloOne.Runtime.Generated;

// «Ядерные тесты.Воркспейс» (VSC-8a): звено-расширение цепочки TBWarehouse из
// модели TestBenchExt (слой 2). Зовёт next() первым — база успевает
// uppercase; суффикс в нижнем регистре доказывает порядок побочек.
[ExtensionOf("TBWarehouse")]
public partial class TBWarehouseEventHandler : TypedDictionaryEventHandler<TBWarehouse>
{
    public override async Task<EventResult> OnBeforeSaveAsync(TBWarehouse record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        // Inner link writes the bag; hydrate Name so work after next() sees
        // the owner's uppercase (typed next() also does this after a Runtime rebuild).
        if (context.Entity is IDictionary<string, object?> bag
            && bag.TryGetValue("Name", out var raw)
            && raw is string stored)
            record.Name = stored;

        if (!string.IsNullOrEmpty(record.Name)
            && record.Name.StartsWith("CHAIN", StringComparison.OrdinalIgnoreCase)
            && context.PreviousResult?.Success == true
            && !record.Name.EndsWith("-ext", StringComparison.Ordinal))
        {
            record.Name += "-ext";
        }
        return EventResult.Ok();
    }
}