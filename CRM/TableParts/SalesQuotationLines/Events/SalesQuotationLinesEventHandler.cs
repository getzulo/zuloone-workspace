#nullable enable
using System;
using System.Collections.Generic;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class SalesQuotationLinesEventHandler : TypedTablePartEventHandler<SalesQuotationLinesTablePartRow>
{
    public override async Task<EventResult> OnFieldChangedAsync(
        SalesQuotationLinesTablePartRow row, string fieldName, object? value, EventContext context)
    {
        if (fieldName is not ("Item" or "Unit" or "Quantity" or "UnitPrice"))
            return EventResult.Ok();

        var header = Owner<SalesQuotation>(context);
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

    private static void Apply(SalesQuotationLinesTablePartRow row, Dictionary<string, object?> applied)
    {
        if (applied.TryGetValue("Unit", out var u) && u is Guid unit) row.Unit = unit;
        if (applied.TryGetValue("Quantity", out var q) && q != null) row.Quantity = Convert.ToDecimal(q);
        if (applied.TryGetValue("UnitPrice", out var p) && p != null) row.UnitPrice = Convert.ToDecimal(p);
        if (applied.TryGetValue("PriceExplanation", out var e))
            row.PriceExplanation = e as string ?? "";
    }
}
