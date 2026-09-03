#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение Inventory моделью GLIntegration: посчитанная недостача и излишек
// попадают в главную книгу. Тот же сервис, что у корректировки остатков:
// обработчик решает только КОГДА.
//
// ЗАЧЕМ ЭТО ОТДЕЛЬНОЕ ЗВЕНО. Инвентаризация — основной способ обнаружить усушку
// и находку. Недостача: драйвер CostingIssue снимает стоимость с ItemCostFifo —
// без ноги в книгу счёт запасов завышался на всю историю. Излишек: Costing
// заводит партию (положительный Amount) — без ноги счёт запасов занижался, а
// найденный товар в книге не существовал.
//
// ДАТА — CountDate, а не DocumentDate: именно ей датируются движения регистра,
// и проводка обязана попасть в тот же период.
//
// ДВЕ НОГИ, ДВА ОПИСАНИЯ. Списание и излишек — разные факты; идемпотентность
// GeneralLedgerService.PostAsync ключуется описанием, поэтому «Stock count {id}»
// и «Stock count surplus {id}» не затирают друг друга. Излишек кредитует СВОЙ
// счёт дохода, а не сторнирует списание: маржа и потери остаются чистыми.
// Нулевая партия (нет истории закупок) — PostSurplusAsync вернёт null.
// Это звено идёт после Costing (слой уже заведён), сумма читается из ItemCostFifo.
public partial class StockCountGLEventHandler : TypedDocumentEventHandler<StockCount>
{
    public override async Task<EventResult> OnAfterPostAsync(StockCount document, EventContext context)
    {
        if (document.Subtype != "Posted") return EventResult.Ok();

        var date = document.CountDate == default ? DateTime.UtcNow.Date : document.CountDate;
        var svc = context.GetService<IInventoryWriteOffGLService>();
        var wo = await svc.PostAsync(document.MetaId, document.Cell, date,
                                    "Stock count " + document.MetaId);
        var su = await svc.PostSurplusAsync(document.MetaId, document.Cell, date,
                                           "Stock count surplus " + document.MetaId);
        var links = context.GetService<IDocumentManager>();
        if (wo.HasValue) await links.AddLinkAsync(document.MetaId, wo.Value);
        if (su.HasValue) await links.AddLinkAsync(document.MetaId, su.Value);

        return EventResult.Ok();
    }
}
