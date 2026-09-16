#nullable enable
using System.Linq;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

public partial class DeliveryScheduleEventHandler : TypedDictionaryEventHandler<DeliverySchedule>
{
    public override async Task<EventResult> OnBeforeSaveAsync(DeliverySchedule record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (string.IsNullOrWhiteSpace(record.Name))
            return EventResult.Cancel("Укажите наименование расписания");
        if (record.Route == Guid.Empty)
            return EventResult.Cancel("Укажите маршрут расписания");
        if (record.Weekday == Weekday.Unspecified)
            return EventResult.Cancel("Укажите день недели");
        if (record.DepartHour < 0 || record.DepartHour > 23)
            return EventResult.Cancel("Час выезда должен быть от 0 до 23");
        if (record.DepartMinute < 0 || record.DepartMinute > 59)
            return EventResult.Cancel("Минута выезда должна быть от 0 до 59");

        if (!record.IsDisabled)
        {
            var clash = (await context.GetService<IDictionaryManager<DeliverySchedule>>()
                    .GetRecordsAsync($"Route = '{record.Route}'"))
                .FirstOrDefault(s => s.MetaId != record.MetaId
                    && !s.IsDisabled
                    && s.Weekday == record.Weekday);
            if (clash is not null)
                return EventResult.Cancel(
                    $"На этот день недели у маршрута уже есть расписание «{clash.Name}»");
        }

        return EventResult.Ok();
    }
}
