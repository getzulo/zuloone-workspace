#nullable enable
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Строка цены. Окна, единица, Calculated — спрашивает IPricingService,
// своего сравнения дат здесь нет.
public partial class PriceListItemEventHandler : TypedDictionaryEventHandler<PriceListItem>
{
    public override async Task<EventResult> OnBeforeSaveAsync(PriceListItem record, bool isNew, EventContext context)
    {
        var error = await context.GetService<IPricingService>().ValidateRowAsync(
            record.MetaId, record.PriceList, record.Item, record.Unit,
            record.Price, record.EffectiveFrom, record.EffectiveTo);
        return error == null ? EventResult.Ok() : EventResult.Cancel(error);
    }
}
