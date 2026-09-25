#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Delta = counted − system. Tx cannot see services; WriteBack of lines on a
// draft save does not reach the database (verified: stock stayed at the old
// balance). So the number is written to QtyDelta via IDataService in OnBeforePost —
// and Tx re-reads the lines from the database AFTER that hook (BuildContextAsync).
//
// Movement date is CountDate. Postings take the header DocumentDate, so before
// posting the header gets CountDate via WriteBack (skill pitfall 4b).
public partial class StockCountEventHandler : TypedDocumentEventHandler<StockCount>
{
    public override async Task<EventResult> OnBeforeCreateAsync(StockCount header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("StockCount");
        if (header.CountDate.Year < 1902)
        {
            var createDay = createDefaults.PickDay(createSeed, "CountDate");
            if (createDay.Year >= 1902) header.CountDate = createDay;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforeSaveAsync(StockCount header, bool isNew, EventContext context){
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;

        if (isNew || header.Subtype != "Posted" || header.MetaId == Guid.Empty)
            return EventResult.Ok();

        var stored = await context.GetService<IDocumentManager>().GetDocumentAsync<StockCount>(header.MetaId);
        if (stored == null) return EventResult.Ok();

        var countDate = stored.CountDate == default ? DateTime.UtcNow.Date : stored.CountDate.Date;
        header.DocumentDate = countDate;
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforePostAsync(StockCount header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<StockCount>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;
        var cell = full != null && full.Cell != Guid.Empty ? full.Cell : header.Cell;
        var stock = context.GetService<ITotalsManager>();
        var data = context.GetService<IDataService>();

        foreach (var line in lines)
        {
            if (line.Item == Guid.Empty || line.MetaId == Guid.Empty) continue;
            var lineCell = line.Cell != Guid.Empty ? line.Cell : cell;
            var bal = await stock.GetBalanceAsync("Stock",
                new Dictionary<string, object?> { ["Item"] = line.Item, ["Cell"] = lineCell });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            var counted = line.BaseQuantity != 0m ? line.BaseQuantity : line.CountedQty;
            var delta = counted - onHand;
            await data.UpdateAsync("TP_StockCountLines", line.MetaId,
                new Dictionary<string, object?> { ["QtyDelta"] = delta });
        }

        return EventResult.Ok();
    }
}
