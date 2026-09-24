#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

public partial class AbcPolicyCellEventHandler : TypedDictionaryEventHandler<AbcPolicyCell>
{
    public override async Task<EventResult> OnBeforeSaveAsync(AbcPolicyCell record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.Profile == Guid.Empty)
            return EventResult.Cancel("Укажите профиль");
        var abc = (record.AbcClass ?? string.Empty).Trim();
        if (abc.Length == 0)
            return EventResult.Cancel("Укажите класс ABC");
        record.AbcClass = abc;
        record.XyzClass = (record.XyzClass ?? string.Empty).Trim();
        if (record.CoverDays < 0)
            return EventResult.Cancel("Покрытие в днях не может быть отрицательным");

        var clash = (await context.GetService<IDictionaryManager<AbcPolicyCell>>().GetRecordsAsync("1 = 1"))
            .FirstOrDefault(c => c.MetaId != record.MetaId
                && c.Profile == record.Profile
                && string.Equals(c.AbcClass, abc, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.XyzClass ?? "", record.XyzClass, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
            return EventResult.Cancel($"Ячейка {abc}/{(record.XyzClass.Length == 0 ? "—" : record.XyzClass)} уже есть в этом профиле");

        return EventResult.Ok();
    }
}
