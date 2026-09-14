#nullable enable
using System.Linq;

namespace ZuloOne.Runtime.Generated;

// Validate a payment to the fund before posting. The SocialInsurance register is
// created with allowNegativeBalance=true, so there is no engine cutoff on the
// balance — we check that the document itself makes sense: an empty payment and
// a line where both shares are zero or negative.
//
// Overpayment to the fund is left unblocked on purpose: advance remittances and
// top-ups after a restated calculation are lawful, and a hard cutoff would make
// them impossible.
//
// Lines are re-read via IDocumentManager: the header event does not carry the
// table part.
public partial class SocialInsurancePaymentEventHandler : TypedDocumentEventHandler<SocialInsurancePayment>
{
    public override async Task<EventResult> OnBeforePostAsync(SocialInsurancePayment document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Paid")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<SocialInsurancePayment>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;

        if (lines.Count == 0)
            return EventResult.Cancel("Заполните строки платежа");

        if (lines.Any(l => l.EmployeeContribution < 0m || l.EmployerContribution < 0m))
            return EventResult.Cancel("Доли взноса не могут быть отрицательными");

        if (lines.All(l => l.EmployeeContribution + l.EmployerContribution == 0m))
            return EventResult.Cancel("Сумма платежа должна быть больше нуля");

        return EventResult.Ok();
    }
}
