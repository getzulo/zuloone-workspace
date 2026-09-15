#nullable enable
using System;
using System.Linq;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

public partial class VehicleEventHandler : TypedDictionaryEventHandler<Vehicle>
{
    public override async Task<EventResult> OnBeforeSaveAsync(Vehicle record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (string.IsNullOrWhiteSpace(record.Name))
            return EventResult.Cancel("Укажите наименование машины");
        if (string.IsNullOrWhiteSpace(record.PlateNumber))
            return EventResult.Cancel("Укажите госномер");
        if (record.Kind == VehicleKind.Unspecified)
            return EventResult.Cancel("Укажите тип машины");
        if (record.CapacityQty < 0m || record.CapacityKg < 0m)
            return EventResult.Cancel("Вместимость не может быть отрицательной");

        var plate = record.PlateNumber.Trim();
        record.PlateNumber = plate;
        var clash = (await context.GetService<IDictionaryManager<Vehicle>>().GetRecordsAsync("1 = 1"))
            .FirstOrDefault(v => v.MetaId != record.MetaId
                && string.Equals(v.PlateNumber, plate, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
            return EventResult.Cancel($"Госномер «{plate}» уже занят машиной «{clash.Name}»");

        return EventResult.Ok();
    }
}
