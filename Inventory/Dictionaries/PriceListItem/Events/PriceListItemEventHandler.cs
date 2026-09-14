#nullable enable
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Price row. Windows, unit, Calculated — asked of IPricingService,
// there is no date comparison here.
public partial class PriceListItemEventHandler : TypedDictionaryEventHandler<PriceListItem>
{
    public override async Task<EventResult> OnBeforeSaveAsync(PriceListItem record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        var error = await context.GetService<IPricingService>().ValidateRowAsync(
            record.MetaId, record.PriceType, record.Item, record.Unit,
            record.Price, record.EffectiveFrom, record.EffectiveTo);
        return error == null ? EventResult.Ok() : EventResult.Cancel(error);
    }
}
