#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Promo earn-rate window. Two live campaigns of the same ItemGroup and the
// same customer type may not overlap: the posting script needs one number
// per line. Empty ItemGroup is every line and may share dates with a
// specific group. Empty CustomerType is every customer and may share dates
// with a specific type. Disabled rows do not occupy the calendar.
public partial class LoyaltyCampaignEventHandler : TypedDictionaryEventHandler<LoyaltyCampaign>
{
    public override async Task<EventResult> OnBeforeCreateAsync(LoyaltyCampaign record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("LoyaltyCampaign");
        if (record.EffectiveFrom.Year < 1902)
        {
            var createDay = createDefaults.PickDay(createSeed, "EffectiveFrom");
            if (createDay.Year >= 1902) record.EffectiveFrom = createDay;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforeSaveAsync(
        LoyaltyCampaign record, bool isNew, EventContext context)
    {
        if (string.IsNullOrWhiteSpace(record.Code))
            return EventResult.Cancel("Укажите код кампании");
        record.Code = record.Code.Trim();
        if (string.IsNullOrWhiteSpace(record.Name))
            return EventResult.Cancel("Укажите название кампании");
        if (record.EarnRate <= 0m)
            return EventResult.Cancel("Курс кампании должен быть больше нуля");
        if (record.EffectiveTo.HasValue && record.EffectiveFrom > record.EffectiveTo.Value)
            return EventResult.Cancel(
                "Окно кампании задано наоборот: дата начала позже даты окончания");

        record.CustomerType = string.IsNullOrWhiteSpace(record.CustomerType)
            ? ""
            : record.CustomerType.Trim();
        if (record.CustomerType.Length > 8)
            return EventResult.Cancel(
                "Тип клиента на кампании длиннее 8 символов — столько же умещает карточка клиента");

        if (!record.IsDisabled)
        {
            var overlap = await context.GetService<ILoyaltyCampaignService>()
                .FindOverlappingAsync(
                    isNew ? Guid.Empty : record.MetaId,
                    record.EffectiveFrom,
                    record.EffectiveTo,
                    record.ItemGroup,
                    record.CustomerType);
            if (overlap is not null)
                return EventResult.Cancel(
                    "На этот период уже есть другая кампания лояльности для этой группы и этого типа клиента");
        }

        return EventResult.Ok();
    }
}
