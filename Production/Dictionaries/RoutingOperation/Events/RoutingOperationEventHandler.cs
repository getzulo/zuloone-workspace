#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

public partial class RoutingOperationEventHandler : TypedDictionaryEventHandler<RoutingOperation>
{
    public override async Task<EventResult> OnBeforeSaveAsync(
        RoutingOperation record, bool isNew, EventContext context)
    {
        if (record.Routing == Guid.Empty)
            return EventResult.Cancel("Укажите маршрут операции");
        if (record.Sequence < 1)
            return EventResult.Cancel("Порядок операции — целое от 1");
        if (string.IsNullOrWhiteSpace(record.Name))
            return EventResult.Cancel("Укажите название операции");
        record.Name = record.Name.Trim();
        if (record.SetupMinutes < 0 || record.RunMinutesPerUnit < 0m)
            return EventResult.Cancel("Минуты операции не могут быть отрицательными");

        var others = await context.GetService<IDictionaryManager<RoutingOperation>>()
            .GetRecordsAsync($"Routing = '{record.Routing}' AND Sequence = {record.Sequence}");
        if (others.Any(x => x.MetaId != record.MetaId))
            return EventResult.Cancel($"Порядок {record.Sequence} на этом маршруте уже занят");

        return EventResult.Ok();
    }
}
