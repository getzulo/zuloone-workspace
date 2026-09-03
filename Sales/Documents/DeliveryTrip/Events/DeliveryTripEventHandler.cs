#nullable enable
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class DeliveryTripEventHandler : TypedDocumentEventHandler<DeliveryTrip>
{
    public override async Task<EventResult> OnBeforePostAsync(DeliveryTrip document, EventContext context)
    {
        if (document.Subtype != "Completed" && document.Subtype != "Dispatched")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<DeliveryTrip>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;
        if (lines.Count == 0)
            return EventResult.Cancel("Добавьте точки рейса");
        if (lines.Any(l => l.SalesOrder == Guid.Empty))
            return EventResult.Cancel("У каждой точки должен быть заказ");

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnAfterPostAsync(DeliveryTrip document, EventContext context)
    {
        if (document.Subtype != "Completed")
            return EventResult.Ok();

        await context.GetService<ISalesFulfillmentService>().CompleteTripAsync(document.MetaId);
        return EventResult.Ok();
    }
}
