#nullable enable
namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for CustomerPayment documents.
// `header` is a typed CustomerPayment entity — access fields directly (header.Number).
// Record events (insert/update/delete/validate) plus the document-only posting
// events are stubbed below. Cancel a transition with EventResult.Cancel("reason");
// document table-part rows are available via document.TableParts["Name"].
public partial class CustomerPaymentEventHandler : TypedDocumentEventHandler<CustomerPayment>
{
    // Building a new document server-side: seed header defaults (number, date).
    public override Task<EventResult> OnBeforeCreateAsync(CustomerPayment header, EventContext context)
        => next(header, context);

    // MIQS BeforeSave: runs before ANY save — insert (isNew) or update.
    public override Task<EventResult> OnBeforeSaveAsync(CustomerPayment header, bool isNew, EventContext context)
        => next(header, isNew, context);

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(CustomerPayment header, bool isNew, EventContext context)
        => next(header, isNew, context);

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(CustomerPayment header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(CustomerPayment header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(CustomerPayment header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(CustomerPayment header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before the document is deleted.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // After the document was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // Before posting: validate the whole document; cancel to block posting.
    //
    // Mirror of VendorPaymentEventHandler in Purchasing. Receivable is set up
    // with allowNegativeBalance=true — customer advances are legal, so there is
    // no engine-level on-hand cutoff here and must not be; only the document
    // itself is validated. Without this, a payment with a NEGATIVE amount
    // posted and INCREASED the debt instead of settling it.
    //
    // Lines are re-read via IDocumentManager: the header event does not receive
    // the table part.
    public override async Task<EventResult> OnBeforePostAsync(CustomerPayment header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        if (header.Subtype != "Paid")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<CustomerPayment>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;

        if (lines.Count == 0)
            return EventResult.Cancel("Заполните строки оплаты");

        if (System.Linq.Enumerable.Any(lines, l => l.Amount <= 0m))
            return EventResult.Cancel("Сумма оплаты должна быть больше нуля");

        return EventResult.Ok();
    }

    // After the document was posted (register movements are written).
    public override Task<EventResult> OnAfterPostAsync(CustomerPayment header, EventContext context)
        => next(header, context);

    // Before unpost/cancel: about to reverse the document's movements.
    public override Task<EventResult> OnBeforeUnpostAsync(CustomerPayment header, EventContext context)
        => next(header, context);

    // After the document's movements were reversed.
    public override Task<EventResult> OnAfterUnpostAsync(CustomerPayment header, EventContext context)
        => next(header, context);

    // Human-readable description shown in lists: put it in context.Data["description"].
    public override async Task<EventResult> OnGenerateDescriptionAsync(CustomerPayment header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        // context.Data["description"] = "CustomerPayment " + header.Number;
        return EventResult.Ok();
    }

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(CustomerPayment header, string errorMessage, EventContext context)
        => next(header, errorMessage, context);

    // A delete failed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => next(recordId, errorMessage, context);
}
