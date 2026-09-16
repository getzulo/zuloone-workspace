#nullable enable
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class PurchaseReturnEventHandler : TypedDocumentEventHandler<PurchaseReturn>
{
    public override async Task<EventResult> OnBeforeSaveAsync(PurchaseReturn header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;

        if (header.OriginalOrder == Guid.Empty)
            return EventResult.Ok();

        var order = await context.GetService<IDocumentManager>()
            .GetDocumentAsync<PurchaseOrder>(header.OriginalOrder);
        if (order is null)
            return EventResult.Ok();

        if (header.Supplier == Guid.Empty)
            header.Supplier = order.Supplier;
        if (header.Location == Guid.Empty)
            header.Location = order.Location;

        if (header.Lines.Count == 0)
        {
            foreach (var line in order.Lines)
            {
                header.Lines.Add(new PurchaseReturnLinesTablePartRow
                {
                    Item = line.Item,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                });
            }
        }

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforePostAsync(PurchaseReturn document, EventContext context)
    {
        var prior = await next(document, context);
        if (!prior.Success) return prior;
        if (document.Subtype != "Posted")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<PurchaseReturn>(document.MetaId)
            ?? document;
        var lines = full.Lines;
        if (lines.Count == 0)
            return EventResult.Cancel("Заполните строки возврата поставщику");
        if (lines.Any(l => l.Quantity <= 0m))
            return EventResult.Cancel("Количество в строке должно быть больше нуля");
        if (full.Supplier == Guid.Empty)
            return EventResult.Cancel("Укажите поставщика");
        if (full.Location == Guid.Empty)
            return EventResult.Cancel("Укажите ячейку, из которой уходит товар");

        var stock = context.GetService<IStockAvailabilityService>();
        foreach (var group in lines.GroupBy(l => l.Item))
        {
            var qty = group.Sum(l => l.Quantity);
            if (await stock.HasSufficientStockAsync(full.Location, group.Key, qty)) continue;
            var onHand = await stock.OnHandAsync(full.Location, group.Key);
            return EventResult.Cancel($"Не хватает остатка в ячейке: нужно {qty}, есть {onHand}");
        }

        return EventResult.Ok();
    }
}
