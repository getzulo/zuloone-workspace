#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// HR extension: payroll accrual is posted to the general ledger —
// Dr payroll expense / Cr employee payable.
// Third consumer of GeneralLedgerService: same mechanics as sales and
// purchasing, only the profile accounts and line captions differ.
//
// Legal entity is taken along Division → LegalEntity: the accrual has
// neither a warehouse nor a counterparty, only Division.
[ExtensionOf("PayrollAccrual")]
public partial class PayrollGLEventHandler : TypedDocumentEventHandler<PayrollAccrual>
{
    public override async Task<EventResult> OnAfterPostAsync(PayrollAccrual document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Posted") return EventResult.Ok();

        var jeId = await PostToLedgerAsync(document, context);
        if (jeId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(PayrollAccrual header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        var accrual = await context.GetService<IDocumentManager>().GetDocumentAsync<PayrollAccrual>(header.MetaId);
        if (accrual == null) return null;

        // Posting amount is the accrual total by lines; the amounts themselves
        // were already computed by the «Accrue payroll» command from hours and
        // the position rate.
        var total = accrual.Lines.Sum(l => l.Amount);
        if (total <= 0m) return null;

        var div = await context.GetService<IDictionaryManager<Division>>().GetRecordAsync(accrual.Division);
        if (div == null) return null;
        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(div.LegalEntity);
        if (le == null) return null;

        return await gl.PostAsync(
            accrual.DocumentDate, le.MetaId, le.Currency, total,
            settings.PayrollExpenseAccountCode, settings.PayrollLiabilityAccountCode,
            "Payroll accrual " + header.MetaId,
            "Расход на оплату труда", "Задолженность перед сотрудниками");
    }
}
