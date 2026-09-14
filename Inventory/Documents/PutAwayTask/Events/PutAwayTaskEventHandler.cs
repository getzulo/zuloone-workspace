#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for PutAwayTask documents.
// `header` is a typed PutAwayTask entity — access fields directly (header.Number).
// Record events (insert/update/delete/validate) plus the document-only posting
// events are stubbed below. Cancel a transition with EventResult.Cancel("reason");
// document table-part rows are available via document.TableParts["Name"].
public partial class PutAwayTaskEventHandler : TypedDocumentEventHandler<PutAwayTask>
{
    // Building a new document server-side: seed header defaults (number, date).
    public override Task<EventResult> OnBeforeCreateAsync(PutAwayTask header, EventContext context)
        => next(header, context);

    // MIQS BeforeSave: runs before ANY save — insert (isNew) or update.
    public override Task<EventResult> OnBeforeSaveAsync(PutAwayTask header, bool isNew, EventContext context)
        => next(header, isNew, context);

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(PutAwayTask header, bool isNew, EventContext context)
        => next(header, isNew, context);

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(PutAwayTask header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(PutAwayTask header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(PutAwayTask header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(PutAwayTask header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before the document is deleted.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // After the document was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // Put-away of received goods: receiving → storage. Two checks of different
    // nature, and they MUST NOT be mixed.
    //
    // First is physics: you cannot put away more than sits in the receiving cell.
    // It ALWAYS runs, the flag does not turn it off. The Stock register allows
    // a negative balance (allowNegativeBalance), so the engine is no help here —
    // the check must live here.
    //
    // Second is policy: receiving is receiving, storage is storage. It is turned
    // on by EnforceWarehouseTasks, because until now cells were free-form, and
    // suddenly forbidding an arbitrary cell would break every existing document.
    //
    // Policy lives in StoreCellService, even though it is in THIS SAME model:
    // a model calling its own service contract once broke the contract assembly,
    // but the platform fixed that — verified by compile and tests. So this is
    // thin orchestration, and "which cell is for what" lives in one place;
    // receipt and sale ask the same thing.
    public override async Task<EventResult> OnBeforePostAsync(PutAwayTask header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        if (header.Subtype != "Confirmed") return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<PutAwayTask>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;
        var fromCell = full?.FromCell ?? header.FromCell;

        if (lines.Count == 0)
            return EventResult.Cancel("Заполните строки задания");

        var cells = context.GetService<IStoreCellService>();
        var enforcing = await cells.IsWarehouseDisciplineOnAsync();
        if (enforcing && !await cells.IsCellAllowedForAsync(fromCell, StoreCellPurpose.Receiving))
            return EventResult.Cancel("Раскладка забирает товар из ячейки ПРИЁМКИ — у выбранной ячейки другое назначение");

        // Demand is counted in BaseQuantity — the register stores the item's base unit.
        // Zero means "unit not specified, no conversion".
        var demand = new Dictionary<Guid, decimal>();
        foreach (var line in lines)
        {
            if (enforcing && !await cells.IsCellAllowedForAsync(line.ToCell, StoreCellPurpose.Storage))
                return EventResult.Cancel("Раскладка кладёт товар в ячейку ХРАНЕНИЯ — у выбранной ячейки другое назначение");

            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            if (qty <= 0m) return EventResult.Cancel("Количество в строке должно быть больше нуля");
            demand[line.Item] = (demand.TryGetValue(line.Item, out var d) ? d : 0m) + qty;
        }

        var totals = context.GetService<ITotalsManager>();
        foreach (var kv in demand)
        {
            var onHand = await totals.GetBalanceAsync("Stock", "Qty",
                new Dictionary<string, object?> { ["Item"] = kv.Key, ["Cell"] = fromCell });
            if (kv.Value > onHand)
                return EventResult.Cancel($"Недостаточно товара в ячейке приёмки: требуется {kv.Value}, в наличии {onHand}");
        }

        return EventResult.Ok();
    }

    // After the document was posted (register movements are written).
    public override Task<EventResult> OnAfterPostAsync(PutAwayTask header, EventContext context)
        => next(header, context);

    // Before unpost/cancel: about to reverse the document's movements.
    public override Task<EventResult> OnBeforeUnpostAsync(PutAwayTask header, EventContext context)
        => next(header, context);

    // After the document's movements were reversed.
    public override Task<EventResult> OnAfterUnpostAsync(PutAwayTask header, EventContext context)
        => next(header, context);

    // Human-readable description shown in lists: put it in context.Data["description"].
    public override async Task<EventResult> OnGenerateDescriptionAsync(PutAwayTask header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        // context.Data["description"] = "PutAwayTask " + header.Number;
        return EventResult.Ok();
    }

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(PutAwayTask header, string errorMessage, EventContext context)
        => next(header, errorMessage, context);

    // A delete failed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => next(recordId, errorMessage, context);
}
