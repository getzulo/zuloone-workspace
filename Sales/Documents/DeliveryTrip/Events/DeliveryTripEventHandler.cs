using System;
#nullable enable
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class DeliveryTripEventHandler : TypedDocumentEventHandler<DeliveryTrip>
{
    public override async Task<EventResult> OnBeforeCreateAsync(DeliveryTrip header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("DeliveryTrip");
        if (header.DeliveryDate.Year < 1902)
        {
            var createDay = createDefaults.PickDay(createSeed, "DeliveryDate");
            if (createDay.Year >= 1902) header.DeliveryDate = createDay;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforeSaveAsync(DeliveryTrip header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;

        var onDate = header.DeliveryDate != default ? header.DeliveryDate : DateTime.UtcNow;
        var stamp = await context.GetService<IDeliveryService>()
            .ResolveStampAsync(header.Route, header.Vehicle, header.Driver, header.Depot, onDate);
        if (header.Vehicle == Guid.Empty && stamp.TryGetValue("Vehicle", out var v) && v is Guid vehicle)
            header.Vehicle = vehicle;
        if (header.Driver == Guid.Empty && stamp.TryGetValue("Driver", out var d) && d is Guid driver)
            header.Driver = driver;
        if (header.Depot == Guid.Empty && stamp.TryGetValue("Depot", out var depot) && depot is Guid store)
            header.Depot = store;

        // Optional document DateTime is non-nullable; MinValue overflows SQL Server.
        // Only on INSERT: a later Update hydrates a partial bag, and clamping
        // missing keys would WriteBack 1901 over ActualDepart the command just set.
        if (isNew)
        {
            if (header.PlannedDepart.Year < 1902) header.PlannedDepart = new DateTime(1901, 1, 1);
            if (header.ActualDepart.Year < 1902) header.ActualDepart = new DateTime(1901, 1, 1);
            if (header.ActualComplete.Year < 1902) header.ActualComplete = new DateTime(1901, 1, 1);
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforePostAsync(DeliveryTrip document, EventContext context)
    {
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Completed" && document.Subtype != "Dispatched")
            return EventResult.Ok();

        var reason = await context.GetService<IDeliveryService>().ValidateTripAsync(document.MetaId);
        if (reason != null)
            return EventResult.Cancel(reason);
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnAfterPostAsync(DeliveryTrip document, EventContext context)
    {
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Completed")
            return EventResult.Ok();

        await context.GetService<ISalesFulfillmentService>().CompleteTripAsync(document.MetaId);
        return EventResult.Ok();
    }
}
