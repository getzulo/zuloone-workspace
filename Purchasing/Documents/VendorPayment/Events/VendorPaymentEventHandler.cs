#nullable enable
using System.Linq;

namespace ZuloOne.Runtime.Generated;

// Проверка оплаты поставщику перед проведением. Кредиторка (Payable) заведена с
// allowNegativeBalance=true — переплата и авансы поставщику законны, поэтому
// движковой отсечки по остатку здесь нет и быть не должно; проверяется только
// осмысленность самого документа: пустая оплата и неположительная сумма.
//
// Строки перечитываются через IDocumentManager: в событие заголовка табличная
// часть не приезжает (тот же приём, что в PurchaseOrder и ProductionOrder).
public partial class VendorPaymentEventHandler : TypedDocumentEventHandler<VendorPayment>
{
    public override async Task<EventResult> OnBeforePostAsync(VendorPayment document, EventContext context)
    {
        if (document.Subtype != "Paid")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<VendorPayment>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;

        if (lines.Count == 0)
            return EventResult.Cancel("Заполните строки оплаты");

        if (lines.Any(l => l.Amount <= 0m))
            return EventResult.Cancel("Сумма оплаты должна быть больше нуля");

        return EventResult.Ok();
    }
}
