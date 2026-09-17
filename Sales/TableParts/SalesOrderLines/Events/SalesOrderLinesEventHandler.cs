#nullable enable
using System;
using System.Collections.Generic;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class SalesOrderLinesEventHandler : TypedTablePartEventHandler<SalesOrderLinesTablePartRow>
{
    public override async Task<EventResult> OnFieldChangedAsync(
        SalesOrderLinesTablePartRow row, string fieldName, object? value, EventContext context)
    {
        var prior = await next(row, fieldName, value, context);
        if (!prior.Success) return prior;

        if (fieldName is not ("Item" or "Unit" or "Quantity" or "UnitPrice"))
            return EventResult.Ok();

        // Persist of Mix rows does not publish the header (would escalate a
        // distributed transaction). Draft pricing runs on the document save.
        var header = Owner<SalesOrder>(context);
        if (header is null)
            return EventResult.Ok();
        var onDate = header.DeliveryDate != default
            ? header.DeliveryDate
            : DateTime.UtcNow;
        var applied = await context.GetService<ISalesLinePricing>().ApplyFieldAsync(
            fieldName,
            row.Item, row.Unit, row.Quantity, row.UnitPrice, row.PriceExplanation,
            header.Customer, header.Contract, onDate);
        Apply(row, applied);
        return EventResult.Ok();
    }

    private static void Apply(SalesOrderLinesTablePartRow row, Dictionary<string, object?> applied)
    {
        if (applied.TryGetValue("Unit", out var u) && u is Guid unit) row.Unit = unit;
        if (applied.TryGetValue("Quantity", out var q) && q != null) row.Quantity = Convert.ToDecimal(q);
        if (applied.TryGetValue("UnitPrice", out var p) && p != null) row.UnitPrice = Convert.ToDecimal(p);
        if (applied.TryGetValue("PriceExplanation", out var e))
            row.PriceExplanation = e as string ?? "";
    }
}
