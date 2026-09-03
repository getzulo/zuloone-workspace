#nullable enable
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Подтверждение заказа: строки, остаток минус уже занятый резерв.
// AllowBackorder в настройках снимает проверку остатка — иначе существующий
// флаг ничего бы не делал. Доставка порождает счёт через сервис: событие
// тонкое и идемпотентное (повтор OnAfterPost не плодит второй счёт).
public partial class SalesOrderEventHandler : TypedDocumentEventHandler<SalesOrder>
{
    public override async Task<EventResult> OnBeforePostAsync(SalesOrder document, EventContext context)
    {
        if (document.Subtype != "Confirmed")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesOrder>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;
        if (lines.Count == 0)
            return EventResult.Cancel("Заполните строки заказа");
        if (lines.Any(l => l.Quantity <= 0m))
            return EventResult.Cancel("Количество в строке должно быть больше нуля");

        var settings = (await context.GetService<IDictionaryManager<SalesSettings>>().GetRecordsAsync("1 = 1"))
            .FirstOrDefault();
        if (settings?.AllowBackorder == true)
            return EventResult.Ok();

        var fulfill = context.GetService<ISalesFulfillmentService>();
        var location = document.Location;
        if (full != null) location = full.Location;
        foreach (var line in lines)
        {
            var free = await fulfill.AvailableQtyAsync(location, line.Item);
            if (free < line.Quantity)
                return EventResult.Cancel(
                    $"Не хватает свободного остатка: нужно {line.Quantity}, свободно {free}");
        }

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnAfterPostAsync(SalesOrder document, EventContext context)
    {
        if (document.Subtype != "Delivered")
            return EventResult.Ok();

        await context.GetService<ISalesFulfillmentService>().InvoiceOrderAsync(document.MetaId);
        return EventResult.Ok();
    }
}
