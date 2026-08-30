#nullable enable
using System;
using System.Collections.Generic;
using ZuloOne.Core.Services;

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
        => Task.FromResult(EventResult.Ok());

    // MIQS BeforeSave: runs before ANY save — insert (isNew) or update.
    public override Task<EventResult> OnBeforeSaveAsync(StockAdjustment header, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(StockAdjustment header, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

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
        => Task.FromResult(EventResult.Ok());

    // After the document was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before posting: reject a write-off that would drive a bin negative (Stock is a
    // double-entry ledger with allowNegativeBalance:true, so the engine no longer
    // guards this). Check on-hand at document.Location for each negative line.
    private static readonly Guid StockRegister = Guid.Parse("83559331-ac7f-46da-87a8-7da599ef6f41");

    public override async Task<EventResult> OnBeforePostAsync(StockAdjustment header, EventContext context)
    {
        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<StockAdjustment>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;

        // Сравнивается с остатком регистра, а он в БАЗОВОЙ единице товара — значит и
        // списание считается по BaseQuantity. Ноль = единица не указана, пересчёта не было.
        var writeOff = new Dictionary<Guid, decimal>();
        foreach (var line in lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            if (qty < 0m)
                writeOff[line.Item] = (writeOff.TryGetValue(line.Item, out var d) ? d : 0m) + (-qty);
        }

        var stock = context.GetService<IRegisterMovementService>();
        foreach (var kv in writeOff)
        {
            var bal = await stock.GetBalanceAsync(StockRegister,
                new Dictionary<string, object?> { ["Item"] = kv.Key, ["Cell"] = header.Cell });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            if (kv.Value > onHand)
                return EventResult.Cancel($"Списание сверх остатка: списывается {kv.Value}, в наличии {onHand}");
        }
        return EventResult.Ok();
    }

    // After the document was posted (register movements are written).
    public override Task<EventResult> OnAfterPostAsync(StockAdjustment header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before unpost/cancel: about to reverse the document's movements.
    public override Task<EventResult> OnBeforeUnpostAsync(StockAdjustment header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the document's movements were reversed.
    public override Task<EventResult> OnAfterUnpostAsync(StockAdjustment header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Human-readable description shown in lists: put it in context.Data["description"].
    public override Task<EventResult> OnGenerateDescriptionAsync(StockAdjustment header, EventContext context)
    {
        // context.Data["description"] = "StockAdjustment " + header.Number;
        return Task.FromResult(EventResult.Ok());
    }

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(StockAdjustment header, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // A delete failed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());
}
