#nullable enable
using System;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение Inventory моделью Costing: пересчёт ВВЕРХ при инвентаризации
// заводит партию себестоимости — тот же безвозмездный приход, что и излишек
// корректировки, и тот же сервис.
//
// Особенность инвентаризации: её складские движения пишет OnBeforePost самого
// документа (там есть доступ к текущему остатку, которого нет в Tx-скрипте), а
// не транзакционный скрипт. Для сервиса это безразлично — он считает нетто по
// уже записанным движениям Stock этого документа, а не по строкам, и потому
// одинаково работает с обоими способами.
//
// Пересчёт ВНИЗ (недостача) сюда не попадает: его списывает драйвер CostingIssue.
public partial class StockCountCostingEventHandler : TypedDocumentEventHandler<StockCount>
{
    public override async Task<EventResult> OnAfterPostAsync(StockCount document, EventContext context)
    {
        if (document.Subtype != "Posted") return EventResult.Ok();

        await context.GetService<ISurplusCostingService>()
            .CaptureSurplusAsync(document.MetaId, DateTime.UtcNow.Date);

        return EventResult.Ok();
    }
}
