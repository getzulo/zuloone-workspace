#nullable enable
using System;
using System.Collections.Generic;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for StockAdjustment documents.
// `header` is a typed StockAdjustment entity — access fields directly (header.Number).
// Record events (insert/update/delete/validate) plus the document-only posting
// events are stubbed below. Cancel a transition with EventResult.Cancel("reason");
// document table-part rows are available via document.TableParts["Name"].
public partial class StockAdjustmentEventHandler : TypedDocumentEventHandler<StockAdjustment>
{
    // Building a new document server-side: seed header defaults (number, date).
    public override Task<EventResult> OnBeforeCreateAsync(StockAdjustment header, EventContext context)
        => next(header, context);

    // MIQS BeforeSave: runs before ANY save — insert (isNew) or update.
    public override Task<EventResult> OnBeforeSaveAsync(StockAdjustment header, bool isNew, EventContext context)
        => next(header, isNew, context);

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(StockAdjustment header, bool isNew, EventContext context)
        => next(header, isNew, context);

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(StockAdjustment header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(StockAdjustment header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(StockAdjustment header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(StockAdjustment header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before the document is deleted.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // After the document was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // Before posting: reject a write-off that would drive a bin negative (Stock is a
    // single-entry register with allowNegativeBalance:true, so the engine does not
    // guard this). Check on-hand at document.Cell for each negative line.

    public override async Task<EventResult> OnBeforePostAsync(StockAdjustment header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<StockAdjustment>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;

        // Compared to the register balance, which is in the item's BASE unit — so
        // the write-off is also counted in BaseQuantity. Zero = unit not specified, no conversion.
        var writeOff = new Dictionary<(Guid Cell, Guid Item), decimal>();
        foreach (var line in lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            if (qty >= 0m) continue;
            var cell = line.Cell != Guid.Empty ? line.Cell : header.Cell;
            var key = (cell, line.Item);
            writeOff[key] = (writeOff.TryGetValue(key, out var d) ? d : 0m) + (-qty);
        }

        var stock = context.GetService<ITotalsManager>();
        foreach (var kv in writeOff)
        {
            var bal = await stock.GetBalanceAsync("Stock",
                new Dictionary<string, object?> { ["Item"] = kv.Key.Item, ["Cell"] = kv.Key.Cell });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            if (kv.Value > onHand)
                return EventResult.Cancel($"Списание сверх остатка: списывается {kv.Value}, в наличии {onHand}");
        }
        return EventResult.Ok();
    }

    // After the document was posted (register movements are written).
    public override Task<EventResult> OnAfterPostAsync(StockAdjustment header, EventContext context)
        => next(header, context);

    // Before unpost/cancel: about to reverse the document's movements.
    public override Task<EventResult> OnBeforeUnpostAsync(StockAdjustment header, EventContext context)
        => next(header, context);

    // After the document's movements were reversed.
    public override Task<EventResult> OnAfterUnpostAsync(StockAdjustment header, EventContext context)
        => next(header, context);

    // Human-readable description shown in lists: put it in context.Data["description"].
    public override async Task<EventResult> OnGenerateDescriptionAsync(StockAdjustment header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        // context.Data["description"] = "StockAdjustment " + header.Number;
        return EventResult.Ok();
    }

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(StockAdjustment header, string errorMessage, EventContext context)
        => next(header, errorMessage, context);

    // A delete failed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => next(recordId, errorMessage, context);
}
