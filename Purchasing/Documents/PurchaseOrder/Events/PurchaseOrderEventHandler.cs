#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
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

        // Налоговый контур НАСТРОЕН, но на дату прихода действующей ставки нет —
        // приход не проводится. Зеркало проверки у счёта продажи, и стоит она в
        // ОТМЕНЯЕМОМ событии по той же причине: в OnAfterPost, где порождается сам
        // расчёт, платформа превращает отказ обработчика в предупреждение в логе:
        // документ проводится, а возмещаемый входной налог пропадает молча.
        if (document.Subtype == "Received")
        {
            var tax = context.GetService<ITaxService>();
            var taxCode = await tax.ResolveDefaultTaxCodeAsync();
            if (taxCode is not null && await tax.ResolveRateAsync(taxCode.Value, TaxPointOf(document)) is null)
                return EventResult.Cancel(
                    $"Налоговый код настроен, но действующей ставки на {TaxPointOf(document):yyyy-MM-dd} нет — приход не проводится");
        }

        return EventResult.Ok();
    }

    /// <summary>Дата налогового события — дата документа; незаполненная датируется
    /// сегодняшним днём ровно так же, как её проставляет IDocumentManager при создании.</summary>
    private static DateTime TaxPointOf(PurchaseOrder document)
        => document.DocumentDate == default ? DateTime.UtcNow.Date : document.DocumentDate.Date;

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

        // Ставка подбирается на ДАТУ ПРИХОДА, не на сегодня: иначе документ и его
        // налог датировались бы по-разному, а оприходование задним числом
        // посчиталось бы по сегодняшней ставке.
        var calc = await context.GetService<ITaxService>()
            .CreateCalculationAsync(legalEntity.Value, "INPUT", taxBase, $"Purchase order {document.Number}", TaxPointOf(document));
        if (calc.HasValue)
            await docs.AddLinkAsync(document.MetaId, calc.Value);

        return EventResult.Ok();
    }
}
