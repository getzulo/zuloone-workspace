#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

public partial class AbcClassBandEventHandler : TypedDictionaryEventHandler<AbcClassBand>
{
    public override async Task<EventResult> OnBeforeSaveAsync(AbcClassBand record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.Profile == Guid.Empty)
            return EventResult.Cancel("Укажите профиль");
        if (record.Axis != "Abc" && record.Axis != "Xyz")
            return EventResult.Cancel("Ось: Abc или Xyz");
        var code = (record.ClassCode ?? string.Empty).Trim();
        if (code.Length == 0)
            return EventResult.Cancel("Укажите код класса");
        record.ClassCode = code;
        if (record.BoundTo != 0m && record.BoundTo < record.BoundFrom)
            return EventResult.Cancel("Верхняя граница не может быть меньше нижней");

        var clash = (await context.GetService<IDictionaryManager<AbcClassBand>>().GetRecordsAsync("1 = 1"))
            .FirstOrDefault(b => b.MetaId != record.MetaId
                && b.Profile == record.Profile
                && string.Equals(b.Axis, record.Axis, StringComparison.OrdinalIgnoreCase)
                && string.Equals(b.ClassCode, code, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
            return EventResult.Cancel($"Класс «{code}» на оси {record.Axis} уже есть в этом профиле");

        return EventResult.Ok();
    }
}
