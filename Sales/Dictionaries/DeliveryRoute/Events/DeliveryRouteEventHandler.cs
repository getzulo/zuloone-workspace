#nullable enable
namespace ZuloOne.Runtime.Generated;

public partial class DeliveryRouteEventHandler : TypedDictionaryEventHandler<DeliveryRoute>
{
    public override async Task<EventResult> OnBeforeSaveAsync(DeliveryRoute record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;
        if (string.IsNullOrWhiteSpace(record.Name))
            return EventResult.Cancel("Укажите наименование маршрута");
        if (record.ArriveRadiusMeters < 0)
            return EventResult.Cancel("Допуск прибытия не может быть отрицательным");
        return EventResult.Ok();
    }
}
