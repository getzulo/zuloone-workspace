#nullable enable
using System;
using System.Linq;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

public partial class DriverEventHandler : TypedDictionaryEventHandler<Driver>
{
    public override async Task<EventResult> OnBeforeSaveAsync(Driver record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (string.IsNullOrWhiteSpace(record.Name))
            return EventResult.Cancel("Укажите ФИО водителя");

        var license = (record.LicenseNumber ?? string.Empty).Trim();
        record.LicenseNumber = license;
        if (license.Length == 0)
            return EventResult.Ok();

        var clash = (await context.GetService<IDictionaryManager<Driver>>().GetRecordsAsync("1 = 1"))
            .FirstOrDefault(d => d.MetaId != record.MetaId
                && string.Equals(d.LicenseNumber, license, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
            return EventResult.Cancel($"Номер прав «{license}» уже у водителя «{clash.Name}»");

        return EventResult.Ok();
    }
}
