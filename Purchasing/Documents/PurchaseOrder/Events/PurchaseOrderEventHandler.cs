#nullable enable
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Purchase order validation: a receipt must have lines and every line a positive
// quantity. Lines are re-loaded via IDocumentManager (the header event does not
// carry table parts).
public partial class PurchaseOrderEventHandler : TypedDocumentEventHandler<PurchaseOrder>
{
    public override async Task<EventResult> OnBeforePostAsync(PurchaseOrder document, EventContext context)
    {
        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<PurchaseOrder>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;

        if (lines.Count == 0)
            return EventResult.Cancel("Заказ без строк не проводится");

        foreach (var line in lines)
        {
            if (line.Quantity <= 0m)
                return EventResult.Cancel("Количество в строке должно быть больше нуля");
        }

        return EventResult.Ok();
    }

    // Оприходование порождает расчёт ВХОДНОГО налога — зеркало выходного у
    // счёта продажи. Тот же сервис и та же необязательность контура: разница
    // ровно в коде направления, поэтому вход и выход не могут разъехаться.
    // Входной налог возмещаемый, поэтому он обязан попасть в тот же леджер, что
    // и выходной, — иначе декларация посчитает налог к уплате с полной выручки.
    public override async Task<EventResult> OnAfterPostAsync(PurchaseOrder document, EventContext context)
    {
        if (document.Subtype != "Received") return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var order = await docs.GetDocumentAsync<PurchaseOrder>(document.MetaId);
        if (order is null || order.Lines.Count == 0) return EventResult.Ok();

        var legalEntity = await context.GetService<IStoreCellService>().GetLegalEntityAsync(order.Location);
        if (legalEntity is null) return EventResult.Ok();

        var pricing = context.GetService<IPricingService>();
        var taxBase = order.Lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice));

        var calc = await context.GetService<ITaxService>()
            .CreateCalculationAsync(legalEntity.Value, "INPUT", taxBase, $"Purchase order {document.Number}");
        if (calc.HasValue)
            await docs.AddLinkAsync(document.MetaId, calc.Value);

        return EventResult.Ok();
    }
}
