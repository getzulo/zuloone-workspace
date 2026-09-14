#nullable enable
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for PayrollAccrual documents.
// `header` is a typed PayrollAccrual entity — access fields directly (header.Number).
// Record events (insert/update/delete/validate) plus the document-only posting
// events are stubbed below. Cancel a transition with EventResult.Cancel("reason");
// document table-part rows are available via document.TableParts["Name"].
public partial class PayrollAccrualEventHandler : TypedDocumentEventHandler<PayrollAccrual>
{
    // Building a new document server-side: seed header defaults (number, date).
    public override Task<EventResult> OnBeforeCreateAsync(PayrollAccrual header, EventContext context)
        => next(header, context);

    // MIQS BeforeSave: runs before ANY save — insert (isNew) or update.
    public override Task<EventResult> OnBeforeSaveAsync(PayrollAccrual header, bool isNew, EventContext context)
        => next(header, isNew, context);

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(PayrollAccrual header, bool isNew, EventContext context)
        => next(header, isNew, context);

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(PayrollAccrual header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(PayrollAccrual header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(PayrollAccrual header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(PayrollAccrual header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before the document is deleted.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // After the document was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // Before posting: validate the whole document; cancel to block posting.
    public override async Task<EventResult> OnBeforePostAsync(PayrollAccrual header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        // if (header.Number == null)
        //     return EventResult.Cancel("Number is required before posting");
        return EventResult.Ok();
    }

    // After the document was posted (register movements are written).
    //
    // A posted payroll accrual spawns a social-insurance CONTRIBUTION accrual —
    // a separate document, like the tax calculation on a sales invoice. Separate
    // document, not movements of this same posting, because contributions are paid
    // to the fund with their own payment, reported on their own form, and can be
    // recalculated (citizenship re-attestation, a rate raised retroactively) without
    // reopening a closed payroll accrual.
    public override async Task<EventResult> OnAfterPostAsync(PayrollAccrual header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        if (header.Subtype != "Posted") return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var accrual = await docs.GetDocumentAsync<PayrollAccrual>(header.MetaId);
        if (accrual is null || accrual.Lines.Count == 0) return EventResult.Ok();

        // Contributions are accrued EXACTLY ONCE per payroll accrual. Posting may
        // run again — a totals driver that writes movements during the document's
        // own posting restarts the chain — and without this guard the second pass
        // would create another contribution document, doubling both the liability
        // to the fund and the employee withholding.
        //
        // An edge carries only endpoint ids; the type lives on the node — we match
        // one against the other. We look for an EDGE from this accrual, not any
        // "contribution" relative in the graph: the family walks links both ways,
        // and a foreign document that joined it by a side path would cancel creating
        // our own.
        var family = await docs.GetDocumentFamilyAsync(header.MetaId);
        var contributionIds = new HashSet<Guid>(
            family.Nodes.Where(n => n.DocTypeMetaId == SocialInsuranceAccrualType).Select(n => n.DocId));
        if (family.Edges.Any(e => e.ParentDocId == header.MetaId && contributionIds.Contains(e.ChildDocId)))
            return EventResult.Ok();

        // One employee may appear on several lines — the contribution is taken
        // from the SUM of accruals, otherwise the wage ceiling is bypassed by
        // splitting lines.
        var gross = new Dictionary<Guid, decimal>();
        foreach (var line in accrual.Lines)
            gross[line.Employee] = (gross.TryGetValue(line.Employee, out var v) ? v : 0m) + line.Amount;

        var si = await context.GetService<ISocialInsuranceService>()
            .CreateAccrualAsync(accrual.Division, gross);
        if (si.HasValue)
            await docs.AddLinkAsync(header.MetaId, si.Value);

        return EventResult.Ok();
    }

    private static readonly Guid SocialInsuranceAccrualType = Guid.Parse("a0d03063-af77-4fd0-886b-223a9731f105");

    // Before unpost/cancel: about to reverse the document's movements.
    public override Task<EventResult> OnBeforeUnpostAsync(PayrollAccrual header, EventContext context)
        => next(header, context);

    // After the document's movements were reversed.
    public override Task<EventResult> OnAfterUnpostAsync(PayrollAccrual header, EventContext context)
        => next(header, context);

    // Human-readable description shown in lists: put it in context.Data["description"].
    public override async Task<EventResult> OnGenerateDescriptionAsync(PayrollAccrual header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        // context.Data["description"] = "PayrollAccrual " + header.Number;
        return EventResult.Ok();
    }

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(PayrollAccrual header, string errorMessage, EventContext context)
        => next(header, errorMessage, context);

    // A delete failed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => next(recordId, errorMessage, context);
}
