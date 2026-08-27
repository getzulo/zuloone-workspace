#nullable enable
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
        => Task.FromResult(EventResult.Ok());

    // MIQS BeforeSave: runs before ANY save — insert (isNew) or update.
    public override Task<EventResult> OnBeforeSaveAsync(StockTransfer header, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(StockTransfer header, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

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
        => Task.FromResult(EventResult.Ok());

    // After the document was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before posting: validate the whole document; cancel to block posting.
    public override Task<EventResult> OnBeforePostAsync(StockTransfer header, EventContext context)
    {
        // if (header.Number == null)
        //     return Task.FromResult(EventResult.Cancel("Number is required before posting"));
        return Task.FromResult(EventResult.Ok());
    }

    // After the document was posted (register movements are written).
    public override Task<EventResult> OnAfterPostAsync(StockTransfer header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before unpost/cancel: about to reverse the document's movements.
    public override Task<EventResult> OnBeforeUnpostAsync(StockTransfer header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the document's movements were reversed.
    public override Task<EventResult> OnAfterUnpostAsync(StockTransfer header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Human-readable description shown in lists: put it in context.Data["description"].
    public override Task<EventResult> OnGenerateDescriptionAsync(StockTransfer header, EventContext context)
    {
        // context.Data["description"] = "StockTransfer " + header.Number;
        return Task.FromResult(EventResult.Ok());
    }

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(StockTransfer header, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // A delete failed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());
}
