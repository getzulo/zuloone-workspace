#nullable enable
using System.Linq;

namespace ZuloOne.Runtime.Generated;

// Проверка платежа в фонд перед проведением. Регистр SocialInsurance заведён с
// allowNegativeBalance=true, поэтому движковой отсечки по остатку нет —
// проверяется осмысленность самого документа: пустой платёж и строка, в которой
// обе доли нулевые или отрицательные.
//
// Переплату в фонд не блокируем намеренно: авансовые перечисления и доплаты по
// уточнённому расчёту законны, а жёсткая отсечка сделала бы их невозможными.
//
// Строки перечитываются через IDocumentManager: в событие заголовка табличная
// часть не приезжает.
public partial class SocialInsurancePaymentEventHandler : TypedDocumentEventHandler<SocialInsurancePayment>
{
    public override async Task<EventResult> OnBeforePostAsync(SocialInsurancePayment document, EventContext context)
    {
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
