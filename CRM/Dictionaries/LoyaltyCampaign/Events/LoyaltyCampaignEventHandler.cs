#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Promo earn-rate window. Two live campaigns may not overlap: the posting
// script needs one number. Disabled rows do not occupy the calendar.
public partial class LoyaltyCampaignEventHandler : TypedDictionaryEventHandler<LoyaltyCampaign>
{
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

        if (!record.IsDisabled)
        {
            var overlap = await context.GetService<ILoyaltyCampaignService>()
                .FindOverlappingAsync(
                    isNew ? Guid.Empty : record.MetaId,
                    record.EffectiveFrom,
                    record.EffectiveTo);
            if (overlap is not null)
                return EventResult.Cancel(
                    "На этот период уже есть другая кампания лояльности");
        }

        return EventResult.Ok();
    }
}
