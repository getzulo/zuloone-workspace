#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// One AttributeCode per profile: two rows would make buyer.profile.X
// depend on row order.
public partial class TaxProfileAttributeEventHandler
    : TypedDictionaryEventHandler<TaxProfileAttribute>
{
    public override async Task<EventResult> OnBeforeSaveAsync(
        TaxProfileAttribute record, bool isNew, EventContext context)
    {
        if (record.TaxProfile == Guid.Empty)
            return EventResult.Cancel("Укажите профиль атрибута");
        if (string.IsNullOrWhiteSpace(record.AttributeCode))
            return EventResult.Cancel("Укажите код атрибута");
        record.AttributeCode = record.AttributeCode.Trim();
        record.Value = (record.Value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(record.Value))
            return EventResult.Cancel("Укажите значение атрибута");

        var others = await context.GetService<IDictionaryManager<TaxProfileAttribute>>()
            .GetRecordsAsync($"TaxProfile = '{record.TaxProfile}'");
        var clash = others.FirstOrDefault(r =>
            r.MetaId != record.MetaId
            && string.Equals(r.AttributeCode, record.AttributeCode, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
            return EventResult.Cancel(
                $"Атрибут «{record.AttributeCode}» уже есть у этого профиля");

        return EventResult.Ok();
    }
}
