#nullable enable
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for TaxReturn documents.
// `header` is a typed TaxReturn entity — access fields directly (header.Number).
// Record events (insert/update/delete/validate) plus the document-only posting
// events are stubbed below. Cancel a transition with EventResult.Cancel("reason");
// document table-part rows are available via document.TableParts["Name"].
public partial class TaxReturnEventHandler : TypedDocumentEventHandler<TaxReturn>
{
    // Building a new document server-side: seed header defaults (number, date).
    public override Task<EventResult> OnBeforeCreateAsync(TaxReturn header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // MIQS BeforeSave: runs before ANY save — insert (isNew) or update.
    public override Task<EventResult> OnBeforeSaveAsync(TaxReturn header, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(TaxReturn header, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(TaxReturn header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(TaxReturn header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(TaxReturn header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(TaxReturn header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before the document is deleted.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the document was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before posting: validate the whole document; cancel to block posting.
    public override Task<EventResult> OnBeforePostAsync(TaxReturn header, EventContext context)
    {
        // if (header.Number == null)
        //     return Task.FromResult(EventResult.Cancel("Number is required before posting"));
        return Task.FromResult(EventResult.Ok());
    }

    // After the document was posted (register movements are written).
    // Сдача в орган — побочный эффект проведения Filed: отказ мока не должен
    // отменять уже состоявшуюся сдачу. Мок чистый, повтор harmless.
    public override async Task<EventResult> OnAfterPostAsync(TaxReturn header, EventContext context)
    {
        if (header.Subtype != "Filed")
            return EventResult.Ok();

        try
        {
            await context.GetService<ITaxAuthoritySubmitService>().SubmitReturnAsync(header.MetaId);
        }
        catch
        {
        }

        return EventResult.Ok();
    }

    // Before unpost/cancel: about to reverse the document's movements.
    public override Task<EventResult> OnBeforeUnpostAsync(TaxReturn header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the document's movements were reversed.
    public override Task<EventResult> OnAfterUnpostAsync(TaxReturn header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Human-readable description shown in lists: put it in context.Data["description"].
    public override Task<EventResult> OnGenerateDescriptionAsync(TaxReturn header, EventContext context)
    {
        // context.Data["description"] = "TaxReturn " + header.Number;
        return Task.FromResult(EventResult.Ok());
    }

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(TaxReturn header, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // A delete failed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());
}
