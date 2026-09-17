#nullable enable
using System.Linq;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

public partial class DeliveryTermEventHandler : TypedDictionaryEventHandler<DeliveryTerm>
{
    public override async Task<EventResult> OnBeforeSaveAsync(DeliveryTerm record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (string.IsNullOrWhiteSpace(record.Code))
            return EventResult.Cancel("Код условия поставки обязателен");
        if (string.IsNullOrWhiteSpace(record.Name))
            return EventResult.Cancel("Наименование условия поставки обязательно");

        record.Code = record.Code.Trim().ToUpperInvariant();

        var duplicate = (await context.GetService<IDictionaryManager<DeliveryTerm>>()
                .GetRecordsAsync($"Code = '{record.Code.Replace("'", "''")}'"))
            .FirstOrDefault(r => r.MetaId != record.MetaId);
        if (duplicate != null)
            return EventResult.Cancel($"Условие поставки с кодом «{record.Code}» уже есть");

        return EventResult.Ok();
    }
}
