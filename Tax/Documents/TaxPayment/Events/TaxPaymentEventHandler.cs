#nullable enable
using System.Linq;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// Проверка оплаты налога перед проведением. Движковой отсечки по остатку
// обязательства здесь нет: налог в регистрах живёт в TaxLedger (начисление) и в
// GL (обязательство), а этот документ только гасит счёт в книге. Проверяется
// осмысленность самого документа: пустая оплата и неположительная сумма.
//
// Строки перечитываются через IDocumentManager: в событие заголовка табличная
// часть не приезжает (тот же приём, что в VendorPayment и TaxCalculation).
public partial class TaxPaymentEventHandler : TypedDocumentEventHandler<TaxPayment>
{
    public override async Task<EventResult> OnBeforePostAsync(TaxPayment document, EventContext context)
    {
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
