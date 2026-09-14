#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// HR extension: a FUND PAYMENT is posted to the general ledger —
// Dr social-insurance fund payable / Cr cash.
//
// Why: contribution accrual credits the fund-payable account twice
// (employee withholding and the employer share — see SocialInsuranceGLEventHandler),
// and there was nothing to debit it with. This pair closes the gap: after the
// payment the fund-payable account matches the SocialInsurance register balance.
//
// Legal entity is taken along Division → LegalEntity, same as the accrual:
// the payment carries the division on the header.
//
// Posting amount is BOTH contribution shares: one payment goes to the fund;
// the split into withheld and employer-accrued exists only for reporting.
public partial class SocialInsurancePaymentGLEventHandler : TypedDocumentEventHandler<SocialInsurancePayment>
{
    public override async Task<EventResult> OnAfterPostAsync(SocialInsurancePayment document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Paid") return EventResult.Ok();

        var jeId = await PostToLedgerAsync(document, context);
        if (jeId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(SocialInsurancePayment header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        var payment = await context.GetService<IDocumentManager>().GetDocumentAsync<SocialInsurancePayment>(header.MetaId);
        if (payment == null) return null;

        var total = payment.Lines.Sum(l => l.EmployeeContribution + l.EmployerContribution);
        if (total <= 0m) return null;

        var div = await context.GetService<IDictionaryManager<Division>>().GetRecordAsync(payment.Division);
        if (div == null) return null;
        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(div.LegalEntity);
        if (le == null) return null;

        return await gl.PostAsync(
            payment.DocumentDate, le.MetaId, le.Currency, total,
            settings.SocialInsurancePayableAccountCode, settings.CashAccountCode,
            "Social insurance payment " + header.MetaId,
            "Задолженность перед фондом соцстраха", "Денежные средства");
    }
}
