#nullable enable
using System.Linq;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

public partial class DeliveryRouteStopEventHandler : TypedDictionaryEventHandler<DeliveryRouteStop>
{
    public override async Task<EventResult> OnBeforeSaveAsync(DeliveryRouteStop record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.Route == Guid.Empty)
            return EventResult.Cancel("Укажите маршрут остановки");
        if (record.Outlet == Guid.Empty)
            return EventResult.Cancel("Укажите торговую точку остановки");
        if (record.Sequence <= 0)
            return EventResult.Cancel("Порядок остановки должен быть больше нуля");
        if (record.DwellMinutes < 0 || record.WindowFromMinutes < 0 || record.WindowToMinutes < 0)
            return EventResult.Cancel("Минуты окна и стоянки не могут быть отрицательными");
        if (record.WindowFromMinutes > 0 && record.WindowToMinutes > 0
            && record.WindowFromMinutes > record.WindowToMinutes)
            return EventResult.Cancel("Окно остановки задано наоборот: начало позже конца");
        if (record.Lat != 0m || record.Lng != 0m)
        {
            if (record.Lat < -90m || record.Lat > 90m)
                return EventResult.Cancel("Широта должна быть от −90 до 90");
            if (record.Lng < -180m || record.Lng > 180m)
                return EventResult.Cancel("Долгота должна быть от −180 до 180");
        }

        var outlet = await context.GetService<IDictionaryManager<CustomerOutlet>>()
            .GetRecordAsync(record.Outlet);
        if (outlet is null)
            return EventResult.Cancel("Торговая точка не найдена");
        if (outlet.IsDisabled)
            return EventResult.Cancel("Нельзя поставить в маршрут отключённую точку");

        var others = (await context.GetService<IDictionaryManager<DeliveryRouteStop>>()
                .GetRecordsAsync($"Route = '{record.Route}'"))
            .Where(s => s.MetaId != record.MetaId && !s.IsDisabled);
        if (others.Any(s => s.Sequence == record.Sequence) && !record.IsDisabled)
            return EventResult.Cancel($"На маршруте уже есть остановка с порядком {record.Sequence}");
        if (others.Any(s => s.Outlet == record.Outlet) && !record.IsDisabled)
            return EventResult.Cancel("Эта точка уже стоит на маршруте");

        return EventResult.Ok();
    }
}
