#nullable enable
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Заголовок типа цены. Правила Kind/цикл/наценка — в IPricingService:
// тот же предикат, которым сервис отказывается считать.
public partial class PriceListEventHandler : TypedDictionaryEventHandler<PriceList>
{
    public override async Task<EventResult> OnBeforeSaveAsync(PriceList record, bool isNew, EventContext context)
    {
        var manager = context.GetService<IDictionaryManager<PriceList>>();
        var duplicate = (await manager
                .GetRecordsAsync($"Name = '{record.Name?.Replace("'", "''")}'"))
            .FirstOrDefault(r => r.MetaId != record.MetaId);
        if (duplicate != null)
            return EventResult.Cancel("Тип цены с таким наименованием уже есть");

        var error = await context.GetService<IPricingService>()
            .ValidateTypeAsync(record.MetaId, (int)record.Kind, record.BasePriceType, record.MarkupPercent);
        return error == null ? EventResult.Ok() : EventResult.Cancel(error);
    }
}
