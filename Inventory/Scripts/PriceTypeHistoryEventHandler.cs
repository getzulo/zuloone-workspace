#nullable enable
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Строка истории цен на карточке номенклатуры. Окна, единица, Calculated —
// спрашивает IPricingService, своего сравнения дат здесь нет.
public partial class PriceTypeHistoryEventHandler : TypedDictionaryEventHandler<LT_PriceTypeHistory>
{
    public override async Task<EventResult> OnBeforeSaveAsync(LT_PriceTypeHistory record, bool isNew, EventContext context)
    {
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
