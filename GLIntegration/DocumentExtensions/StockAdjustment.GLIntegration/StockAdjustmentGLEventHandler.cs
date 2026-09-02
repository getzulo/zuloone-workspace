#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение Inventory моделью GLIntegration: СПИСАНИЕ запасов попадает в
// главную книгу — Dr списание запасов / Cr запасы.
//
// Зачем: приход дебетовал счёт запасов (PurchaseGLEventHandler), продажа его
// кредитовала через себестоимость (SalesGLEventHandler), а бой, недостача и
// прочее выбытие мимо продажи не попадали в книгу вообще. Стоимость уходила из
// регистра ItemCostFifo и оставалась на счёте запасов навсегда: книга завышала
// запас ровно на всё списанное за историю.
//
// ПОЧЕМУ НЕ COGS. Себестоимость продаж — это стоимость ПРОДАННОГО, и валовая
// маржа считается по ней. Бой и недостача продажей не являются: свалив их в тот
// же счёт, мы исказили бы маржу на величину потерь. Поэтому у списания свой счёт
// (InventoryWriteOffAccountCode). Не настроен — проводки нет, как и у всякой
// другой ненастроенной ноги: разноска best-effort и ронять документ не должна.
//
// Сумма НЕ пересчитывается по строкам: списание себестоимости уже сделал драйвер
// CostingIssue, и его движения по ItemCostFifo лежат в базе с DocumentMetaId
// этого документа. Читается ФАКТ — тот же приём, что в разноске себестоимости
// продаж, и по той же причине: метод оценки (FIFO/AVG) живёт в настройках, и
// повторять его здесь значит гарантированно разъехаться с учётом запаса.
//
// Излишек сюда не попадает: у него движения ItemCostFifo положительные, и сумма
// выбытия получается нулевой. Заведение партии на излишек — забота Costing.
public partial class StockAdjustmentGLEventHandler : TypedDocumentEventHandler<StockAdjustment>
{
    public override async Task<EventResult> OnAfterPostAsync(StockAdjustment document, EventContext context)
    {
        if (document.Subtype != "Posted") return EventResult.Ok();

        var jeId = await context.GetService<IInventoryWriteOffGLService>()
            .PostAsync(document.MetaId, document.Cell, document.DocumentDate,
                       "Stock adjustment " + document.MetaId);
        if (jeId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

        return EventResult.Ok();
    }
}
