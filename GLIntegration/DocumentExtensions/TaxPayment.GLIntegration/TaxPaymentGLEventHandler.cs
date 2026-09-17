#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// GLIntegration extension of Tax: a TAX PAYMENT hits the general
// ledger — Dr VAT payable / Cr cash.
//
// Why: VAT accrual credits the liability account (TaxCalculationGL), and
// there was nothing to debit it with. In the tax ledger the tax stays accrued
// (that is a filing fact), while the book liability grew without bound — the
// same gap already closed for VendorPayment and PayrollPayment.
//
// LEGAL ENTITY — A HEADER FIELD. If unset, the payment posts as before and
// there is no journal entry (best-effort: missing setup must not break existing
// documents).
[ExtensionOf("TaxPayment")]
public partial class TaxPaymentGLEventHandler : TypedDocumentEventHandler<TaxPayment>
{
    public override async Task<EventResult> OnAfterPostAsync(TaxPayment document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Paid") return EventResult.Ok();

        var jeId = await PostToLedgerAsync(document, context);
        if (jeId.HasValue)
        {
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

            // A government-authority mock must not reverse a journal entry already posted.
            try
            {
                await context.GetService<ITaxAuthoritySubmitService>().SubmitPaymentAsync(document.MetaId);
            }
            catch
            {
            }
        }

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(TaxPayment header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        var payment = await context.GetService<IDocumentManager>().GetDocumentAsync<TaxPayment>(header.MetaId);
        if (payment == null) return null;
        if (payment.LegalEntity == Guid.Empty) return null;

        var total = payment.Lines.Sum(l => l.Amount);
        if (total <= 0m) return null;

        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(payment.LegalEntity);
        if (le == null) return null;

        return await gl.PostAsync(
            payment.DocumentDate, le.MetaId, le.Currency, total,
            settings.VatPayableAccountCode, settings.CashAccountCode,
            "Tax payment " + header.MetaId,
            "НДС к уплате", "Денежные средства",
            "FIN,TAX");
    }
}
