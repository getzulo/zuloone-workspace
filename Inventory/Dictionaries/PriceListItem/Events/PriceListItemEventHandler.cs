using System;
#nullable enable
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Price row. Windows, unit, Calculated — asked of IPricingService,
// there is no date comparison here.
public partial class PriceListItemEventHandler : TypedDictionaryEventHandler<PriceListItem>
{
    public override async Task<EventResult> OnBeforeCreateAsync(PriceListItem record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("PriceListItem");
        if (record.EffectiveFrom is not { } from || from.Year < 1902)
        {
            var createDay = createDefaults.PickDay(createSeed, "EffectiveFrom");
            if (createDay.Year >= 1902) record.EffectiveFrom = createDay;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforeSaveAsync(PriceListItem record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        var error = await context.GetService<IPricingService>().ValidateRowAsync(
            record.MetaId, record.PriceType, record.Item, record.Unit,
            record.Price, record.EffectiveFrom, record.EffectiveTo);
        return error == null ? EventResult.Ok() : EventResult.Cancel(error);
    }
}
