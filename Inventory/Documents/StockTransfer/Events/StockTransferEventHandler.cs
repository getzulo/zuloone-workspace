#nullable enable
using System;
using System.Collections.Generic;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for StockTransfer documents.
// `header` is a typed StockTransfer entity — access fields directly (header.Number).
// Record events (insert/update/delete/validate) plus the document-only posting
// events are stubbed below. Cancel a transition with EventResult.Cancel("reason");
// document table-part rows are available via document.TableParts["Name"].
public partial class StockTransferEventHandler : TypedDocumentEventHandler<StockTransfer>
{
    // Building a new document server-side: seed header defaults (number, date).
    public override Task<EventResult> OnBeforeCreateAsync(StockTransfer header, EventContext context)
        => next(header, context);

    // MIQS BeforeSave: runs before ANY save — insert (isNew) or update.
    public override Task<EventResult> OnBeforeSaveAsync(StockTransfer header, bool isNew, EventContext context)
        => next(header, isNew, context);

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(StockTransfer header, bool isNew, EventContext context)
        => next(header, isNew, context);

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(StockTransfer header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(StockTransfer header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(StockTransfer header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(StockTransfer header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before the document is deleted.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // After the document was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // Before posting: validate the whole document; cancel to block posting.
    //
    // GUARD AGAINST GOING NEGATIVE. Stock is declared allowNegativeBalance=true —
    // the engine does not reject a minus, so the check must live here. Every
    // other outbound document has it (write-off, issue, pick, put-away,
    // sale, production), but transfer did not.
    //
    // Why this is worse than a normal negative: a transfer is a PAIR of movements
    // for one item, net zero, so the costing driver does not look at it at all.
    // Transfer 100 with 5 on hand and you get −95 in the source cell and +100
    // in the target WITH NO cost layer, and a later shipment from the target
    // consumes layers of someone else's real stock.
    //
    // Demand is counted in BaseQuantity: the register balance is in the base
    // unit, and "2 boxes" would otherwise pass against 12 pieces on the shelf.
    // Lines of the same item are summed — splitting across lines does not
    // bypass the check.
    public override async Task<EventResult> OnBeforePostAsync(StockTransfer header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        if (header.Subtype != "Posted")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<StockTransfer>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;
        var headerFrom = full?.FromCell ?? header.FromCell;

        var need = new Dictionary<(Guid Cell, Guid Item), decimal>();
        foreach (var line in lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            if (qty <= 0m)
                return EventResult.Cancel("Количество перемещения должно быть больше нуля");
            var cell = line.FromCell != Guid.Empty ? line.FromCell : headerFrom;
            var key = (cell, line.Item);
            need[key] = (need.TryGetValue(key, out var d) ? d : 0m) + qty;
        }

        var stock = context.GetService<ITotalsManager>();
        foreach (var kv in need)
        {
            var bal = await stock.GetBalanceAsync("Stock",
                new Dictionary<string, object?> { ["Item"] = kv.Key.Item, ["Cell"] = kv.Key.Cell });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            if (kv.Value > onHand)
                return EventResult.Cancel($"Перемещение сверх остатка: перемещается {kv.Value}, в наличии {onHand}");
        }

        return EventResult.Ok();
    }

    // After the document was posted (register movements are written).
    public override Task<EventResult> OnAfterPostAsync(StockTransfer header, EventContext context)
        => next(header, context);

    // Before unpost/cancel: about to reverse the document's movements.
    public override Task<EventResult> OnBeforeUnpostAsync(StockTransfer header, EventContext context)
        => next(header, context);

    // After the document's movements were reversed.
    public override Task<EventResult> OnAfterUnpostAsync(StockTransfer header, EventContext context)
        => next(header, context);

    // Human-readable description shown in lists: put it in context.Data["description"].
    public override async Task<EventResult> OnGenerateDescriptionAsync(StockTransfer header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        // context.Data["description"] = "StockTransfer " + header.Number;
        return EventResult.Ok();
    }

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(StockTransfer header, string errorMessage, EventContext context)
        => next(header, errorMessage, context);

    // A delete failed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => next(recordId, errorMessage, context);
}
