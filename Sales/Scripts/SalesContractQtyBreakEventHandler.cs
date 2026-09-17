#nullable enable
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

public partial class SalesContractQtyBreakEventHandler : TypedDictionaryEventHandler<LT_SalesContractQtyBreak>
{
    public override async Task<EventResult> OnBeforeSaveAsync(
        LT_SalesContractQtyBreak record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if ((record.SalesContract ?? Guid.Empty) == Guid.Empty)
            return EventResult.Cancel("Укажите договор");
        if ((record.MinQty ?? 0m) <= 0m)
            return EventResult.Cancel("Порог количества должен быть больше нуля");
        var pct = record.DiscountPercent ?? 0m;
        if (pct < 0m || pct > 100m)
            return EventResult.Cancel("Скидка за количество должна быть в диапазоне от 0 до 100%");

        var item = record.Item ?? Guid.Empty;
        var min = record.MinQty ?? 0m;
        var siblings = await context.GetService<ILinkTableManager>()
            .GetRecordsAsync<LT_SalesContractQtyBreak>(new Dictionary<string, object?>
            {
                ["SalesContract"] = record.SalesContract,
            });
        if (siblings.Any(other =>
            other.MetaId != record.MetaId
            && (other.Item ?? Guid.Empty) == item
            && (other.MinQty ?? 0m) == min))
            return EventResult.Cancel("Для этого товара и порога количества на договоре уже есть скидка");

        return EventResult.Ok();
    }
}
