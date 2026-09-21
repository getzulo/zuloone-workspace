#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// One door for the morning wave. Handlers and commands do not walk stops,
// schedules or other trips themselves — they ask here.
public partial class DeliveryService
{
    private readonly IDocumentManager _documents;
    private readonly IDictionaryManager<DeliveryRoute> _routes;
    private readonly IDictionaryManager<DeliveryRouteStop> _stops;
    private readonly IDictionaryManager<DeliverySchedule> _schedules;
    private readonly IDictionaryManager<Vehicle> _vehicles;
    private readonly IDictionaryManager<Driver> _drivers;
    private readonly IDataService _data;

    public DeliveryService(
        IDocumentManager documents,
        IDictionaryManager<DeliveryRoute> routes,
        IDictionaryManager<DeliveryRouteStop> stops,
        IDictionaryManager<DeliverySchedule> schedules,
        IDictionaryManager<Vehicle> vehicles,
        IDictionaryManager<Driver> drivers,
        IDataService data)
    {
        _documents = documents;
        _routes = routes;
        _stops = stops;
        _schedules = schedules;
        _vehicles = vehicles;
        _drivers = drivers;
        _data = data;
    }

    /// <summary>ISO weekday of the date. Monday=1 … Sunday=7.</summary>
    public int WeekdayOf(DateTime onDate)
        => onDate.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)onDate.DayOfWeek;

    /// <summary>Live schedule of the route on that weekday. Empty — none.</summary>
    public async Task<Guid> FindScheduleAsync(Guid routeId, DateTime onDate)
    {
        if (routeId == Guid.Empty) return Guid.Empty;
        var day = (Weekday)WeekdayOf(onDate);
        var match = (await _schedules.GetRecordsAsync($"Route = '{routeId}'"))
            .FirstOrDefault(s => !s.IsDisabled && s.Weekday == day);
        return match?.MetaId ?? Guid.Empty;
    }

    /// <summary>Fill empty crew / depot from the route, then the weekday schedule.</summary>
    public async Task<Dictionary<string, object?>> ResolveStampAsync(
        Guid routeId, Guid vehicleId, Guid driverId, Guid depotId, DateTime onDate)
    {
        var stamp = new Dictionary<string, object?>();
        if (routeId == Guid.Empty) return stamp;

        var route = await _routes.GetRecordAsync(routeId);
        if (route is not null)
        {
            if (vehicleId == Guid.Empty && route.DefaultVehicle != Guid.Empty)
                stamp["Vehicle"] = route.DefaultVehicle;
            if (driverId == Guid.Empty && route.DefaultDriver != Guid.Empty)
                stamp["Driver"] = route.DefaultDriver;
            if (depotId == Guid.Empty && route.Depot != Guid.Empty)
                stamp["Depot"] = route.Depot;
        }

        var scheduleId = await FindScheduleAsync(routeId, onDate);
        if (scheduleId == Guid.Empty) return stamp;
        var schedule = await _schedules.GetRecordAsync(scheduleId);
        if (schedule is null) return stamp;

        if (!stamp.ContainsKey("Vehicle") && vehicleId == Guid.Empty && schedule.Vehicle != Guid.Empty)
            stamp["Vehicle"] = schedule.Vehicle;
        if (!stamp.ContainsKey("Driver") && driverId == Guid.Empty && schedule.Driver != Guid.Empty)
            stamp["Driver"] = schedule.Driver;
        return stamp;
    }

    /// <summary>Null — the trip may be dispatched.</summary>
    public async Task<string?> ValidateTripAsync(Guid tripId)
    {
        var trip = await _documents.GetDocumentAsync<DeliveryTrip>(tripId);
        if (trip is null) return "Рейс не найден";
        if (trip.Lines.Count == 0)
            return "Добавьте точки рейса";
        if (trip.Lines.Any(l => l.SalesOrder is not Guid id || id == Guid.Empty))
            return "У каждой точки должен быть заказ";

        if (trip.Vehicle == Guid.Empty)
            return "Укажите машину рейса";
        var vehicle = await _vehicles.GetRecordAsync(trip.Vehicle);
        if (vehicle is null) return "Машина не найдена";
        if (vehicle.IsDisabled) return "Машина отключена";

        if (trip.Driver == Guid.Empty)
            return "Укажите водителя рейса";
        var driver = await _drivers.GetRecordAsync(trip.Driver);
        if (driver is null) return "Водитель не найден";
        if (driver.IsDisabled) return "Водитель отключён";

        if (trip.Route != Guid.Empty)
        {
            var route = await _routes.GetRecordAsync(trip.Route);
            if (route is null) return "Маршрут не найден";
            if (route.IsDisabled) return "Маршрут отключён";

            var liveStops = (await _stops.GetRecordsAsync($"Route = '{trip.Route}'"))
                .Where(s => !s.IsDisabled)
                .ToList();
            var outlets = new HashSet<Guid>(liveStops.Select(s => s.Outlet));
            foreach (var line in trip.Lines)
            {
                var outlet = line.Outlet;
                if (outlet == Guid.Empty && line.SalesOrder is Guid orderId && orderId != Guid.Empty)
                {
                    var order = await _documents.GetDocumentAsync<SalesOrder>(orderId);
                    outlet = order?.Outlet ?? Guid.Empty;
                }
                if (outlet == Guid.Empty || !outlets.Contains(outlet))
                    return "Заказ рейса не принадлежит остановке маршрута";
            }
        }

        var busy = await CrewBusyAsync(trip.Vehicle, trip.Driver, trip.DeliveryDate, trip.MetaId);
        if (busy != null) return busy;

        if (vehicle.CapacityQty > 0m)
        {
            var demand = 0m;
            foreach (var line in trip.Lines)
            {
                if (line.SalesOrder is not Guid demandOrderId || demandOrderId == Guid.Empty) continue;
                var order = await _documents.GetDocumentAsync<SalesOrder>(demandOrderId);
                if (order is null) continue;
                demand += order.Lines.Sum(l => l.Quantity);
            }
            if (demand > vehicle.CapacityQty)
                return $"Сумма {demand} превышает вместимость машины {vehicle.CapacityQty}";
        }

        return null;
    }

    /// <summary>Add confirmed orders of the route outlets on the trip date.
    /// Returns how many new stops were appended.</summary>
    public async Task<int> FillTripFromRouteAsync(Guid tripId)
    {
        var trip = await _documents.GetDocumentAsync<DeliveryTrip>(tripId);
        if (trip is null || trip.Route == Guid.Empty) return 0;

        var liveStops = (await _stops.GetRecordsAsync($"Route = '{trip.Route}'"))
            .Where(s => !s.IsDisabled)
            .OrderBy(s => s.Sequence)
            .ToList();
        if (liveStops.Count == 0) return 0;

        var taken = new HashSet<Guid>(trip.Lines
            .Select(l => l.SalesOrder)
            .OfType<Guid>()
            .Where(id => id != Guid.Empty));
        var day = (trip.DeliveryDate == default ? DateTime.UtcNow : trip.DeliveryDate).Date;
        var orders = await _data.QueryAsync("SalesOrder", "Subtype = 'Confirmed'");
        var added = 0;
        foreach (var stop in liveStops)
        {
            foreach (var row in orders)
            {
                var orderId = AsGuid(row, "MetaId");
                if (orderId == Guid.Empty || taken.Contains(orderId)) continue;
                if (AsGuid(row, "Outlet") != stop.Outlet) continue;
                var route = AsGuid(row, "Route");
                if (route != Guid.Empty && route != trip.Route) continue;
                var when = AsDate(row, "DeliveryDate");
                if (when.Date != day) continue;

                trip.Lines.Add(new DeliveryTripLinesTablePartRow
                {
                    SalesOrder = orderId,
                    Outlet = stop.Outlet,
                    StopSequence = stop.Sequence,
                    PlannedFromMinutes = stop.WindowFromMinutes,
                    PlannedToMinutes = stop.WindowToMinutes,
                    DwellMinutes = stop.DwellMinutes,
                });
                taken.Add(orderId);
                added++;
            }
        }

        if (added > 0)
            await _documents.SaveDocumentAsync(trip);
        return added;
    }

    /// <summary>Create a draft trip per live schedule of that weekday.
    /// Skips a route that already has a Draft or Dispatched trip on the date.
    /// Returns how many trips were created.</summary>
    public async Task<int> PlanWaveAsync(DateTime onDate)
    {
        var day = (Weekday)WeekdayOf(onDate);
        var schedules = (await _schedules.GetRecordsAsync("1 = 1"))
            .Where(s => !s.IsDisabled && s.Weekday == day)
            .ToList();
        var existing = await _data.QueryAsync("DeliveryTrip", "Subtype = 'Draft' OR Subtype = 'Dispatched'");
        var takenRoutes = new HashSet<Guid>();
        foreach (var row in existing)
        {
            if (AsDate(row, "DeliveryDate").Date != onDate.Date) continue;
            var route = AsGuid(row, "Route");
            if (route != Guid.Empty) takenRoutes.Add(route);
        }

        var created = 0;
        foreach (var schedule in schedules)
        {
            if (takenRoutes.Contains(schedule.Route)) continue;
            var route = await _routes.GetRecordAsync(schedule.Route);
            if (route is null || route.IsDisabled) continue;

            var trip = await _documents.NewDocumentAsync<DeliveryTrip>();
            trip.DeliveryDate = onDate.Date;
            trip.Route = schedule.Route;
            trip.Vehicle = schedule.Vehicle != Guid.Empty ? schedule.Vehicle : route.DefaultVehicle;
            trip.Driver = schedule.Driver != Guid.Empty ? schedule.Driver : route.DefaultDriver;
            trip.Depot = route.Depot;
            await _documents.SaveDocumentAsync(trip);
            await FillTripFromRouteAsync(trip.MetaId);
            takenRoutes.Add(schedule.Route);
            created++;
        }
        return created;
    }

    private async Task<string?> CrewBusyAsync(Guid vehicleId, Guid driverId, DateTime onDate, Guid excludeTripId)
    {
        var rows = await _data.QueryAsync("DeliveryTrip", "Subtype = 'Dispatched'");
        foreach (var row in rows)
        {
            if (AsGuid(row, "MetaId") == excludeTripId) continue;
            if (AsDate(row, "DeliveryDate").Date != onDate.Date) continue;
            if (vehicleId != Guid.Empty && AsGuid(row, "Vehicle") == vehicleId)
                return "Машина уже в другом рейсе на эту дату";
            if (driverId != Guid.Empty && AsGuid(row, "Driver") == driverId)
                return "Водитель уже в другом рейсе на эту дату";
        }
        return null;
    }

    private static Guid AsGuid(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var raw) || raw is null) return Guid.Empty;
        if (raw is Guid g) return g;
        return Guid.TryParse(raw.ToString(), out var parsed) ? parsed : Guid.Empty;
    }

    private static DateTime AsDate(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var raw) || raw is null) return default;
        if (raw is DateTime dt) return dt;
        return DateTime.TryParse(raw.ToString(), out var parsed) ? parsed : default;
    }
}
