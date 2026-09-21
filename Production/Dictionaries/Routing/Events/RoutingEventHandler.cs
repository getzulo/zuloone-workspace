#nullable enable
using System;
using System.Threading.Tasks;

namespace ZuloOne.Runtime.Generated;

public partial class RoutingEventHandler : TypedDictionaryEventHandler<Routing>
{
    public override Task<EventResult> OnBeforeSaveAsync(
        Routing record, bool isNew, EventContext context)
    {
        if (string.IsNullOrWhiteSpace(record.Name))
            return Task.FromResult(EventResult.Cancel("Укажите название маршрута"));
        record.Name = record.Name.Trim();
        if (record.Product == Guid.Empty)
            return Task.FromResult(EventResult.Cancel("Укажите изделие маршрута"));
        return Task.FromResult(EventResult.Ok());
    }
}
