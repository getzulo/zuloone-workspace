#nullable enable
using System;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение Inventory моделью Costing: излишек корректировки заводит партию
// себестоимости. Направление зависимости обязано быть именно таким — Costing
// зависит от Inventory, обратной зависимости нет и быть не может (был бы цикл),
// поэтому обработчик живёт здесь, а не в самом документе.
//
// OnAfterPost, а не транзакционный скрипт: цена берётся из текущего остатка
// партий, а это чтение БД — в синхронный и чистый GetTransactions оно не влезает.
// К этому моменту складские движения уже записаны, и сервис считает нетто по ним.
//
// Недостача (чистый минус) сюда не попадает: её списывает драйвер CostingIssue.
// Дата партии — DocumentDate документа, не день нажатия «Провести».
public partial class StockAdjustmentCostingEventHandler : TypedDocumentEventHandler<StockAdjustment>
{
    public override async Task<EventResult> OnAfterPostAsync(StockAdjustment document, EventContext context)
    {
        if (document.Subtype != "Posted") return EventResult.Ok();

        var date = document.DocumentDate == default ? DateTime.UtcNow.Date : document.DocumentDate.Date;
        await context.GetService<ISurplusCostingService>()
            .CaptureSurplusAsync(document.MetaId, date);

        return EventResult.Ok();
    }
}
