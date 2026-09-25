#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Dated tax attributes of a party. Overlapping windows of the same
// party are refused here; TaxService still refuses to pick when two
// rows match — same split as TaxRegistration.
public partial class TaxProfileEventHandler : TypedDictionaryEventHandler<TaxProfile>
{
    public override async Task<EventResult> OnBeforeCreateAsync(TaxProfile record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("TaxProfile");
        if (record.EffectiveFrom.Year < 1902)
        {
            var createDay = createDefaults.PickDay(createSeed, "EffectiveFrom");
            if (createDay.Year >= 1902) record.EffectiveFrom = createDay;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforeSaveAsync(
        TaxProfile record, bool isNew, EventContext context)
    {
        var type = NormalizePartyType(record.PartyType);
        if (type is null)
            return EventResult.Cancel(
                "Тип стороны: LegalEntity, Customer или Supplier");
        record.PartyType = type;

        if (string.IsNullOrWhiteSpace(record.Name))
            return EventResult.Cancel("Укажите название профиля");
        record.Name = record.Name.Trim();
        if (record.PartyId == Guid.Empty)
            return EventResult.Cancel("Укажите сторону профиля");
        if (record.EffectiveTo.HasValue && record.EffectiveFrom > record.EffectiveTo.Value)
            return EventResult.Cancel(
                "Окно профиля задано наоборот: дата начала позже даты окончания");

        if (!record.IsDisabled)
        {
            var overlap = await context.GetService<ITaxService>()
                .FindOverlappingProfileAsync(
                    record.PartyType, record.PartyId,
                    isNew ? Guid.Empty : record.MetaId,
                    record.EffectiveFrom, record.EffectiveTo);
            if (overlap is not null)
                return EventResult.Cancel(
                    $"На этот период уже есть профиль {overlap.Name} " +
                    $"с {overlap.EffectiveFrom:yyyy-MM-dd}");
        }

        return EventResult.Ok();
    }

    private static string? NormalizePartyType(string? raw)
    {
        var value = (raw ?? "").Trim();
        if (value.Equals("LegalEntity", StringComparison.OrdinalIgnoreCase))
            return "LegalEntity";
        if (value.Equals("Customer", StringComparison.OrdinalIgnoreCase))
            return "Customer";
        if (value.Equals("Supplier", StringComparison.OrdinalIgnoreCase))
            return "Supplier";
        return null;
    }
}
