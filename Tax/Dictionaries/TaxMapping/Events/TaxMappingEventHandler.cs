#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Pins a TaxCode to a source Guid. Overlapping windows of the same
// SourceType+SourceId are refused so the engine has one number.
public partial class TaxMappingEventHandler : TypedDictionaryEventHandler<TaxMapping>
{
    public override async Task<EventResult> OnBeforeSaveAsync(
        TaxMapping record, bool isNew, EventContext context)
    {
        var type = NormalizeSourceType(record.SourceType);
        if (type is null)
            return EventResult.Cancel(
                "Тип источника: Item, ItemGroup, Customer, Supplier или LegalEntity");
        record.SourceType = type;

        if (record.SourceId == Guid.Empty)
            return EventResult.Cancel("Укажите источник сопоставления");
        if (record.TaxCode == Guid.Empty)
            return EventResult.Cancel("Укажите налоговый код сопоставления");
        if (record.EffectiveTo.HasValue && record.EffectiveFrom > record.EffectiveTo.Value)
            return EventResult.Cancel(
                "Окно сопоставления задано наоборот: дата начала позже даты окончания");

        if (!record.IsDisabled)
        {
            var overlap = await context.GetService<ITaxService>()
                .FindOverlappingMappingAsync(
                    record.SourceType, record.SourceId,
                    isNew ? Guid.Empty : record.MetaId,
                    record.EffectiveFrom, record.EffectiveTo);
            if (overlap is not null)
                return EventResult.Cancel(
                    "На этот период уже есть сопоставление для этого источника");
        }

        return EventResult.Ok();
    }

    private static string? NormalizeSourceType(string? raw)
    {
        var value = (raw ?? "").Trim();
        if (value.Equals("Item", StringComparison.OrdinalIgnoreCase)) return "Item";
        if (value.Equals("ItemGroup", StringComparison.OrdinalIgnoreCase)) return "ItemGroup";
        if (value.Equals("Customer", StringComparison.OrdinalIgnoreCase)) return "Customer";
        if (value.Equals("Supplier", StringComparison.OrdinalIgnoreCase)) return "Supplier";
        if (value.Equals("LegalEntity", StringComparison.OrdinalIgnoreCase)) return "LegalEntity";
        return null;
    }
}
