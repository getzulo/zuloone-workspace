#nullable enable
namespace ZuloOne.Runtime.Generated;

// Handler for TBItem. Override only the hooks you need.
// Chain of Command: the rightmost (highest-layer) override runs first;
// next(...) continues to the lower link and returns its result.
// Work AFTER next() sees what they wrote. Forgetting next() is ZOCOC001.
// [Replace] swallows the chain on purpose.
//
// Суффикс Ext в имени КЛАССА обязателен, и это не стиль. Все модели попадают в
// один IDE-проект (ZuloOne.Workspace.csproj), а звено — partial-класс: тёзка
// владельца из TestBench сливается с ним в ОДИН тип, и общий хук даёт CS0111.
// Платформа этого не ловит, она собирает каждую модель отдельной сборкой —
// поэтому models/compile остаётся зелёным, пока dotnet build красный.
// Переименование безопасно: envelope привязан по objectName, а класс рантайм
// находит по базовому типу.
[ExtensionOf("TBItem")]
public partial class TBItemExtEventHandler : TypedDictionaryEventHandler<TBItem>
{

    public override async Task<EventResult> OnAfterSaveAsync(TBItem record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnAfterLoadAsync(TBItem record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        return EventResult.Ok();
    }
}
