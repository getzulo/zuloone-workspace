#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение Inventory моделью GLIntegration: посчитанная НЕДОСТАЧА попадает в
// главную книгу — Dr списание запасов / Cr запасы. Тот же сервис, что у
// корректировки остатков и отпуска: обработчик решает только КОГДА.
//
// ЗАЧЕМ ЭТО ОТДЕЛЬНОЕ ЗВЕНО. Инвентаризация — основной способ обнаружить усушку,
// и до сих пор она была единственным списанием, не доходившим до книги. Движение
// по Stock документ пишет сам (StockCountEventHandler), драйвер CostingIssue
// снимает соответствующую стоимость с ItemCostFifo — а на счёте запасов она
// оставалась навсегда, и книга завышала запас на всю посчитанную недостачу за
// историю. Ровно тот дефект, ради которого InventoryWriteOffGLService и заведён;
// он просто не был подключён сюда.
//
// ДАТА — CountDate, а не DocumentDate: именно ей StockCountEventHandler
// датирует движения регистра, и проводка обязана попасть в тот же период, иначе
// подсистема и книга разъедутся ровно так, как это было с UtcNow.
//
// Излишек сюда не попадает: у него движения ItemCostFifo положительные, сумма
// выбытия получается нулевой, и сервис возвращает null. Заведение партии на
// излишек — забота Costing (StockCount.Costing, executionOrder 10; это звено
// идёт двадцатым, то есть после того, как слой уже заведён).
public partial class StockCountGLEventHandler : TypedDocumentEventHandler<StockCount>
{
    public override async Task<EventResult> OnAfterPostAsync(StockCount document, EventContext context)
    {
        if (document.Subtype != "Posted") return EventResult.Ok();

        var date = document.CountDate == default ? DateTime.UtcNow.Date : document.CountDate;
        var jeId = await context.GetService<IInventoryWriteOffGLService>()
            .PostAsync(document.MetaId, document.Cell, date,
                       "Stock count " + document.MetaId);
        if (jeId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

        return EventResult.Ok();
    }
}
