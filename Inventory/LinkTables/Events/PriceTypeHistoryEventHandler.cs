#nullable enable
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Price-history row on the item card. Windows, unit, Calculated —
// asked of IPricingService, there is no date comparison here.
public partial class PriceTypeHistoryEventHandler : TypedDictionaryEventHandler<LT_PriceTypeHistory>
{
    public override async Task<EventResult> OnBeforeSaveAsync(LT_PriceTypeHistory record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        var error = await context.GetService<IPricingService>().ValidateRowAsync(
            record.MetaId,
            record.PriceType ?? Guid.Empty,
            record.Item ?? Guid.Empty,
            record.Unit ?? Guid.Empty,
            record.Price ?? 0m,
            record.EffectiveFrom,
            record.EffectiveTo);
        return error == null ? EventResult.Ok() : EventResult.Cancel(error);
    }
}
