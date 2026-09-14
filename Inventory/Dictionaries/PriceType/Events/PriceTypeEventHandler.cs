#nullable enable
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Price type header. Kind/cycle/markup rules live in IPricingService:
// the same predicate the service uses to refuse to calculate.
public partial class PriceTypeEventHandler : TypedDictionaryEventHandler<PriceType>
{
    public override async Task<EventResult> OnBeforeSaveAsync(PriceType record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        var manager = context.GetService<IDictionaryManager<PriceType>>();
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
