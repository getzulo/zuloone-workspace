#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// Дельта = факт − система. Tx сервисов не видит, WriteBack строк на сохранении
// черновика до базы не доезжает (проверено: склад оставался на старом остатке).
// Поэтому число пишется в QtyDelta через IDataService в OnBeforePost — а Tx
// читает строки ЗАНОВО из базы уже после этого хука (BuildContextAsync).
//
// Дата движений — CountDate. Проводки берут DocumentDate шапки, поэтому перед
// проведением шапка получает CountDate WriteBack'ом (ловушка 4б скилла).
public partial class StockCountEventHandler : TypedDocumentEventHandler<StockCount>
{
    public override async Task<EventResult> OnBeforeSaveAsync(StockCount header, bool isNew, EventContext context)
    {
        if (isNew || header.Subtype != "Posted" || header.MetaId == Guid.Empty)
            return EventResult.Ok();

        var stored = await context.GetService<IDocumentManager>().GetDocumentAsync<StockCount>(header.MetaId);
        if (stored == null) return EventResult.Ok();

        var countDate = stored.CountDate == default ? DateTime.UtcNow.Date : stored.CountDate.Date;
        header.DocumentDate = countDate;
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforePostAsync(StockCount header, EventContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<StockCount>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;
        var cell = full != null && full.Cell != Guid.Empty ? full.Cell : header.Cell;
        var stock = context.GetService<ITotalsManager>();
        var data = context.GetService<IDataService>();

        foreach (var line in lines)
        {
            if (line.Item == Guid.Empty || line.MetaId == Guid.Empty) continue;
            var bal = await stock.GetBalanceAsync("Stock",
                new Dictionary<string, object?> { ["Item"] = line.Item, ["Cell"] = cell });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            var counted = line.BaseQuantity != 0m ? line.BaseQuantity : line.CountedQty;
            var delta = counted - onHand;
            await data.UpdateAsync("TP_StockCountLines", line.MetaId,
                new Dictionary<string, object?> { ["QtyDelta"] = delta });
        }

        return EventResult.Ok();
    }
}
