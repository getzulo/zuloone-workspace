using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Events;

namespace ZuloOne.Runtime.Generated;

// «Ядерные тесты.Документы»: проведение без склада отклоняется (OnBeforePost,
// диспетчеризуется DocumentPostingService.SetStatusAsync ДО смены статуса).
public class TBStockDocEventHandler : DocumentEventHandler
{
    public override async Task<EventResult> OnBeforePostAsync(EventContext context){
        var prior = await next(context);
        if (!prior.Success) return prior;

        var header = context.Entity as IDictionary<string, object?>;
        object? raw = null;
        header?.TryGetValue("Warehouse", out raw);
        var warehouse = raw is Guid g ? g
            : Guid.TryParse(raw?.ToString(), out var parsed) ? parsed
            : Guid.Empty;
        return warehouse == Guid.Empty
            ? EventResult.Cancel("Warehouse is required for posting")
            : EventResult.Ok();
    }
}