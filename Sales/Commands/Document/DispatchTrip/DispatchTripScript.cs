using System;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

public partial class DispatchTripCommand
{
    public override async Task ExecuteAsync(DeliveryTrip document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<DeliveryTrip>(document.MetaId);
        if (full == null) return;

        var delivery = context.GetService<IDeliveryService>();
        var reason = await delivery.ValidateTripAsync(full.MetaId);
        if (reason != null)
        {
            context.AddClientAction(ClientAction.Message(reason));
            return;
        }

        var scheduleId = await delivery.FindScheduleAsync(full.Route, full.DeliveryDate);
        if (scheduleId != Guid.Empty)
        {
            var schedule = await context.GetService<IDictionaryManager<DeliverySchedule>>()
                .GetRecordAsync(scheduleId);
            if (schedule != null)
                full.PlannedDepart = full.DeliveryDate.Date
                    .AddHours(schedule.DepartHour)
                    .AddMinutes(schedule.DepartMinute);
        }
        full.ActualDepart = DateTime.UtcNow;
        full.Subtype = DeliveryTrip.Subtypes.Dispatched;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Рейс отправлен."));
    }
}
