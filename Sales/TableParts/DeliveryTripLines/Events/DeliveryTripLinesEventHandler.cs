#nullable enable
using System.Linq;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

public partial class DeliveryTripLinesEventHandler : TypedTablePartEventHandler<DeliveryTripLinesTablePartRow>
{
    public override async Task<EventResult> OnFieldChangedAsync(
        DeliveryTripLinesTablePartRow row, string fieldName, object? value, EventContext context)
    {
        var prior = await next(row, fieldName, value, context);
        if (!prior.Success) return prior;
        var orderId = row.SalesOrder ?? Guid.Empty;
        if (fieldName != "SalesOrder" || orderId == Guid.Empty)
            return EventResult.Ok();

        var order = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesOrder>(orderId);
        if (order is null) return EventResult.Ok();
        if (row.Outlet == Guid.Empty)
            row.Outlet = order.Outlet;

        var trip = Owner<DeliveryTrip>(context);
        if (trip is not null && trip.Route != Guid.Empty && row.Outlet != Guid.Empty
            && (row.StopSequence is null || row.StopSequence <= 0))
        {
            var stop = (await context.GetService<IDictionaryManager<DeliveryRouteStop>>()
                    .GetRecordsAsync($"Route = '{trip.Route}'"))
                .Where(s => !s.IsDisabled && s.Outlet == row.Outlet)
                .OrderBy(s => s.Sequence)
                .FirstOrDefault();
            if (stop is not null)
                row.StopSequence = stop.Sequence;
        }
        return EventResult.Ok();
    }
}
