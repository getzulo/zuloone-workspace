#nullable enable
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

public partial class SalesContractPriceEventHandler : TypedDictionaryEventHandler<LT_SalesContractPrice>
{
    public override async Task<EventResult> OnBeforeSaveAsync(
        LT_SalesContractPrice record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if ((record.Price ?? 0m) <= 0m)
            return EventResult.Cancel("Цена товара по договору должна быть больше нуля");
        if ((record.Item ?? Guid.Empty) == Guid.Empty)
            return EventResult.Cancel("Укажите товар");
        if ((record.Unit ?? Guid.Empty) == Guid.Empty)
            return EventResult.Cancel("Укажите единицу цены");
        if ((record.SalesContract ?? Guid.Empty) == Guid.Empty)
            return EventResult.Cancel("Укажите договор");

        var from = record.EffectiveFrom ?? DateTime.MinValue;
        var to = record.EffectiveTo ?? DateTime.MaxValue;
        if (from.Date > to.Date)
            return EventResult.Cancel("Дата начала цены позже даты окончания");

        var siblings = await context.GetService<ILinkTableManager>()
            .GetRecordsAsync<LT_SalesContractPrice>(new Dictionary<string, object?>
            {
                ["SalesContract"] = record.SalesContract,
                ["Item"] = record.Item,
                ["Unit"] = record.Unit,
            });
        var clash = siblings.FirstOrDefault(other =>
            other.MetaId != record.MetaId
            && WindowsOverlap(record.EffectiveFrom, record.EffectiveTo, other.EffectiveFrom, other.EffectiveTo));
        if (clash != null)
            return EventResult.Cancel(
                "Для этого товара в этом договоре и этой единице уже есть цена на пересекающийся период");

        return EventResult.Ok();
    }

    private static bool WindowsOverlap(DateTime? aFrom, DateTime? aTo, DateTime? bFrom, DateTime? bTo)
        => (aFrom?.Date ?? DateTime.MinValue.Date) <= (bTo?.Date ?? DateTime.MaxValue.Date)
        && (bFrom?.Date ?? DateTime.MinValue.Date) <= (aTo?.Date ?? DateTime.MaxValue.Date);
}
