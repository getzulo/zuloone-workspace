#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// GLIntegration extension of Sales: a CUSTOMER PAYMENT hits the general
// ledger — Dr cash / Cr receivables.
//
// Why: issuing an invoice debits receivables (SalesGLEventHandler), the tax
// calculation adds VAT to them (TaxCalculationGLEventHandler) — and there was
// nothing to credit them with. The receivables account in the books grew by
// all tax-inclusive revenue over history, while the Receivable register was
// cleared by the payment. This is the most expensive half of the same gap
// already closed for payroll and the social-insurance fund.
//
// IMPORTANT ABOUT THE AMOUNT. The Receivable register is kept WITHOUT tax,
// while book receivables are WITH tax (invoice + VAT leg). So a payment for
// the full tax-inclusive amount will close the book account correctly, but
// will drive the register balance negative by the tax amount. Until the
// register and the books keep receivables on different bases, they cannot
// fully agree: closing this seam requires either VAT in the register, or
// dropping the VAT leg in the books. This closes the book side; the register
// side is a separate wiki note, section «What is not there yet».
//
// LEGAL ENTITY — A HEADER FIELD: the payment has neither a warehouse nor a
// source invoice, and the customer has no link to a legal entity. If unset,
// the payment still clears the register as before, and there is no posting.
[ExtensionOf("CustomerPayment")]
public partial class CustomerPaymentGLEventHandler : TypedDocumentEventHandler<CustomerPayment>
{
    public override async Task<EventResult> OnAfterPostAsync(CustomerPayment document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Paid") return EventResult.Ok();

        var jeId = await PostToLedgerAsync(document, context);
        if (jeId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(CustomerPayment header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        var payment = await context.GetService<IDocumentManager>().GetDocumentAsync<CustomerPayment>(header.MetaId);
        if (payment == null) return null;
        if (payment.LegalEntity == Guid.Empty) return null;

        var total = payment.Lines.Sum(l => l.Amount);
        if (total <= 0m) return null;

        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(payment.LegalEntity);
        if (le == null) return null;

        return await gl.PostAsync(
            payment.DocumentDate, le.MetaId, le.Currency, total,
            settings.CashAccountCode, settings.ArAccountCode,
            "Customer payment " + header.MetaId,
            "Денежные средства", "Дебиторка покупателя");
    }
}
