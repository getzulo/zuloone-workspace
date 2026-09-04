#nullable enable
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Новый склад при уже включённой дисциплине сразу получает двор
// (приёмка / хранение / отбор). Без флага ничего не плодит: тесты и старые
// склады сами рисуют ячейки, и первая Storage должна остаться их, а не наша.
public partial class StoreEventHandler : TypedDictionaryEventHandler<Store>
{
    public override async Task<EventResult> OnAfterSaveAsync(Store record, bool isNew, EventContext context)
    {
        var cells = context.GetService<IStoreCellService>();
        if (await cells.IsWarehouseDisciplineOnAsync())
            await cells.EnsureYardAsync(record.MetaId);
        return EventResult.Ok();
    }
}
