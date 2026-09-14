#nullable enable
using System.Linq;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// Validate a tax payment before posting. There is no engine cutoff on the
// liability balance: tax in the registers lives in TaxLedger (accrual) and in
// GL (liability), and this document only settles the book account. We check
// that the document itself makes sense: an empty payment and a non-positive
// amount.
//
// Lines are re-read via IDocumentManager: the header event does not carry the
// table part (the same pattern as VendorPayment and TaxCalculation).
public partial class TaxPaymentEventHandler : TypedDocumentEventHandler<TaxPayment>
{
    public override async Task<EventResult> OnBeforePostAsync(TaxPayment document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Paid")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<TaxPayment>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;

        if (lines.Count == 0)
            return EventResult.Cancel("Заполните строки оплаты");

        if (lines.Any(l => l.Amount <= 0m))
            return EventResult.Cancel("Сумма оплаты должна быть больше нуля");

        return EventResult.Ok();
    }
}
