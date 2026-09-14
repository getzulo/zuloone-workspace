#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// GLIntegration extension of Purchasing: a VENDOR PAYMENT hits the
// general ledger — Dr payables / Cr cash.
//
// Why: receiving a purchase order credits the payable account (PurchaseGLEventHandler),
// and there was nothing to debit it with. The Payable register was cleared by the
// payment, while the ledger grew without bound — the same gap already closed for
// payroll payout and the social-insurance fund payment. The payment document existed,
// but its ledger leg was skipped.
//
// LEGAL ENTITY — A HEADER FIELD, NOT DERIVED FROM REFERENCES. The payment has
// neither a warehouse nor a division, and the supplier has no link to a legal
// entity — there is nothing to derive the posting target from. So the payment
// carries the legal entity itself, as an optional field: if unset, the payment
// still clears the register as before, and there is simply no posting (the same
// best-effort policy as every other leg).
public partial class VendorPaymentGLEventHandler : TypedDocumentEventHandler<VendorPayment>
{
    public override async Task<EventResult> OnAfterPostAsync(VendorPayment document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Paid") return EventResult.Ok();

        var jeId = await PostToLedgerAsync(document, context);
        if (jeId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(VendorPayment header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        var payment = await context.GetService<IDocumentManager>().GetDocumentAsync<VendorPayment>(header.MetaId);
        if (payment == null) return null;
        if (payment.LegalEntity == Guid.Empty) return null;

        var total = payment.Lines.Sum(l => l.Amount);
        if (total <= 0m) return null;

        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(payment.LegalEntity);
        if (le == null) return null;

        return await gl.PostAsync(
            payment.DocumentDate, le.MetaId, le.Currency, total,
            settings.PayableAccountCode, settings.CashAccountCode,
            "Vendor payment " + header.MetaId,
            "Кредиторка перед поставщиком", "Денежные средства");
    }
}
