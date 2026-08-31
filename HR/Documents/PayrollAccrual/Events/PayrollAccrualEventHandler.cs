#nullable enable
using System.Collections.Generic;
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
        => Task.FromResult(EventResult.Ok());

    // MIQS BeforeSave: runs before ANY save — insert (isNew) or update.
    public override Task<EventResult> OnBeforeSaveAsync(PayrollAccrual header, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(PayrollAccrual header, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

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
        => Task.FromResult(EventResult.Ok());

    // After the document was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before posting: validate the whole document; cancel to block posting.
    public override Task<EventResult> OnBeforePostAsync(PayrollAccrual header, EventContext context)
    {
        // if (header.Number == null)
        //     return Task.FromResult(EventResult.Cancel("Number is required before posting"));
        return Task.FromResult(EventResult.Ok());
    }

    // After the document was posted (register movements are written).
    //
    // Проведённое начисление ФОТ порождает начисление ВЗНОСОВ соцстраха —
    // отдельным документом, как налоговый расчёт у счёта продажи. Отдельный
    // документ, а не движения этой же проводки, потому что взносы платятся в
    // фонд своим платежом, отчитываются своей формой и могут быть пересчитаны
    // (переаттестация гражданства, задним числом поднятая ставка) без
    // переоткрытия закрытого начисления ФОТ.
    public override async Task<EventResult> OnAfterPostAsync(PayrollAccrual header, EventContext context)
    {
        if (header.Subtype != "Posted") return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var accrual = await docs.GetDocumentAsync<PayrollAccrual>(header.MetaId);
        if (accrual is null || accrual.Lines.Count == 0) return EventResult.Ok();

        // Один сотрудник может встречаться в нескольких строках — взнос берётся
        // с СУММЫ начислений, иначе потолок базы обходится дроблением строк.
        var gross = new Dictionary<Guid, decimal>();
        foreach (var line in accrual.Lines)
            gross[line.Employee] = (gross.TryGetValue(line.Employee, out var v) ? v : 0m) + line.Amount;

        var si = await context.GetService<ISocialInsuranceService>()
            .CreateAccrualAsync(accrual.Division, gross);
        if (si.HasValue)
            await docs.AddLinkAsync(header.MetaId, si.Value);

        return EventResult.Ok();
    }

    // Before unpost/cancel: about to reverse the document's movements.
    public override Task<EventResult> OnBeforeUnpostAsync(PayrollAccrual header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the document's movements were reversed.
    public override Task<EventResult> OnAfterUnpostAsync(PayrollAccrual header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Human-readable description shown in lists: put it in context.Data["description"].
    public override Task<EventResult> OnGenerateDescriptionAsync(PayrollAccrual header, EventContext context)
    {
        // context.Data["description"] = "PayrollAccrual " + header.Number;
        return Task.FromResult(EventResult.Ok());
    }

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(PayrollAccrual header, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // A delete failed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());
}
