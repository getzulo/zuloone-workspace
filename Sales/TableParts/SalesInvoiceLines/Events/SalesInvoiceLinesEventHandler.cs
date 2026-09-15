#nullable enable
using System;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Same live fill as the sales-order line: Item → default Unit + customer price.
// Owner<SalesInvoice> is the in-memory header.
public partial class SalesInvoiceLinesEventHandler : TypedTablePartEventHandler<SalesInvoiceLinesTablePartRow>
{
    public override async Task<EventResult> OnFieldChangedAsync(
        SalesInvoiceLinesTablePartRow row, string fieldName, object? value, EventContext context)
    {
        var prior = await next(row, fieldName, value, context);
        if (!prior.Success) return prior;

        if (fieldName is not ("Item" or "Unit"))
            return EventResult.Ok();

        var header = Owner<SalesInvoice>(context);
        await FillAsync(row, header, fieldName, context);
        return EventResult.Ok();
    }

    private static async Task FillAsync(
        SalesInvoiceLinesTablePartRow row, SalesInvoice? header, string fieldName, EventContext context)
    {
        if (row.Item == Guid.Empty) return;

        if (fieldName == "Item")
        {
            var item = await context.GetService<IDictionaryManager<Item>>().GetRecordAsync(row.Item);
            if (item is not null && item.UnitOfMeasure != Guid.Empty)
                row.Unit = item.UnitOfMeasure;
            if (row.Quantity <= 0m) row.Quantity = 1m;
        }

        if (row.Unit == Guid.Empty) return;

        var pricing = context.GetService<IPricingService>();
        var customer = header?.Customer ?? Guid.Empty;
        var contractId = header?.Contract ?? Guid.Empty;
        var onDate = header != null && header.DocumentDate != default
            ? header.DocumentDate
            : DateTime.UtcNow;

        decimal? price = null;
        if (contractId != Guid.Empty)
        {
            var contract = await context.GetService<IDictionaryManager<SalesContract>>()
                .GetRecordAsync(contractId);
            if (contract is not null && contract.PriceType != Guid.Empty)
                price = await pricing.ResolveForTypeAsync(row.Item, row.Unit, contract.PriceType, onDate);
        }
        price ??= await pricing.ResolveSalePriceAsync(
            row.Item, row.Unit, customer == Guid.Empty ? null : customer, onDate);
        if (price != null) row.UnitPrice = price.Value;
    }
}
