#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Three order rules. First — auto-expand components: if AutoExpandBom is on in
// module settings, a new order expands the BOM itself, and the same-named
// command stays as the manual alternative (refill after a quantity change).
// Second — output validation: the order does not post without components, with
// a non-positive quantity, or when a component is short on the cell. Third —
// output costing: the Costing driver on Stock already writes off consumed
// component cost (a net minus on the item), but does not open a layer for the
// finished good — a positive net is not its concern (that can be a receipt or
// a zero-cost surplus). The order itself opens that layer, symmetrically to how
// ReceiptCostTx/ReceiptFifoTx open it for purchasing.
//
// Components and quantity are re-read via IDocumentManager (the header event
// does not carry the table part). Balance is checked on Stock physical
// dimensions via ITotalsManager (no engine check: Stock is a one-sided
// register, allowNegativeBalance=true).
public partial class ProductionOrderEventHandler : TypedDocumentEventHandler<ProductionOrder>
{
    public override async Task<EventResult> OnBeforeSaveAsync(ProductionOrder header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;
        var block = await context.GetService<ITradeProfileService>().ProductionBlockReasonAsync();
        if (block != null) return EventResult.Cancel(block);
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnAfterSaveAsync(ProductionOrder header, bool isNew, EventContext context){
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;

        // Create only: saving the expanded lines below will fire this same event
        // again, and without the guard that would recurse. Already filled lines
        // are left alone — manual input wins over the setting.
        if (!isNew || header.Product == Guid.Empty || header.Quantity <= 0m)
            return EventResult.Ok();

        var settings = (await context.GetService<IDictionaryManager<ProductionSettings>>()
            .GetRecordsAsync("1 = 1")).FirstOrDefault();
        if (settings == null || !settings.AutoExpandBom) return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<ProductionOrder>(header.MetaId);
        if (full == null || full.Components.Count > 0) return EventResult.Ok();

        // On BASE quantity and from the re-read document: the BOM is normalized
        // to the finished good's stock unit, while the header quantity may be in any.
        var outputQty = full.BaseQuantity != 0m ? full.BaseQuantity : full.Quantity;
        var need = await context.GetService<IBomService>().ExpandByProductAsync(full.Product, outputQty);
        if (need.Count == 0) return EventResult.Ok();

        foreach (var kv in need)
            full.Components.Add(new ProductionOrderComponentsTablePartRow { Component = kv.Key, QtyRequired = kv.Value });

        await docs.SaveDocumentAsync(full);
        var stamped = await docs.GetDocumentAsync<ProductionOrder>(header.MetaId);
        if (stamped != null && stamped.Operations.Count == 0)
            await context.GetService<IRoutingService>().StampOperationsAsync(header.MetaId);

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforePostAsync(ProductionOrder document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        var block = await context.GetService<ITradeProfileService>().ProductionBlockReasonAsync();
        if (block != null) return EventResult.Cancel(block);

        if (document.Subtype != "Finished" && document.Subtype != "Released")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<ProductionOrder>(document.MetaId);
        var components = full?.Components ?? document.Components;
        var quantity = full?.Quantity ?? document.Quantity;
        var location = full?.Location ?? document.Location;

        if (quantity <= 0m)
            return EventResult.Cancel("Количество выпуска должно быть больше нуля");

        if (components.Count == 0)
            return EventResult.Cancel("Заполните компоненты (разверните спецификацию)");

        // Compared to the register balance, which is in the item's BASE unit — so
        // demand is also taken from BaseQuantity. Zero = unit not specified, no
        // conversion (that is how BOM-expanded lines arrive: BomService already
        // returns demand in the component's stock unit).
        var demand = ComponentDemand(components);

        var stock = context.GetService<ITotalsManager>();
        var own = new Dictionary<Guid, decimal>();
        foreach (var row in await stock.QueryMovementsAsync(
            "Stock", $"[DocumentMetaId] = '{document.MetaId}'"))
        {
            if (row["Item"] is not Guid item) continue;
            var qty = row["Qty"] is null ? 0m : Convert.ToDecimal(row["Qty"]);
            own[item] = (own.TryGetValue(item, out var d) ? d : 0m) + qty;
        }

        foreach (var kv in demand)
        {
            var bal = await stock.GetBalanceAsync("Stock",
                new Dictionary<string, object?> { ["Item"] = kv.Key, ["Cell"] = location });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            var alreadyTaken = own.TryGetValue(kv.Key, out var taken) ? taken : 0m;
            var available = onHand - alreadyTaken;
            if (kv.Value > available)
                return EventResult.Cancel($"Недостаточно компонента на ячейке: требуется {kv.Value}, в наличии {available}");
        }

        return EventResult.Ok();
    }

    // Costing has already written off the components (Stock net is negative —
    // an issue). The finished good has no layer: we open it ourselves — at the
    // ACTUAL cost of the written-off components.
    //
    // WHY ACTUAL, NOT AVERAGE. Previously this computed Amount/Quantity average
    // on ItemCostFifo. It lied twice. First, by this moment the driver has
    // ALREADY written off the components (its EndDocument runs inside posting),
    // so the average was taken from the balance AFTER the issue, not from what
    // went into production. Second, the average diverges from FIFO across
    // several layers at different prices — and the driver writes off by the
    // method from settings.
    //
    // Actual cost lives where sales COGS posting reads it: ItemCostFifo
    // movements tagged with this document. Negative amounts in them are exactly
    // what the engine took off the layers. Taking them, output is valued by the
    // same method as the issue, and inventory value stays value-neutral under
    // any setting — by construction, not by coincidence.
    public override async Task<EventResult> OnAfterPostAsync(ProductionOrder document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Finished")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<ProductionOrder>(document.MetaId);
        var product = full?.Product ?? document.Product;
        var quantity = full?.Quantity ?? document.Quantity;
        var baseQuantity = full?.BaseQuantity ?? document.BaseQuantity;
        var outputQty = baseQuantity != 0m ? baseQuantity : quantity;

        var totals = context.GetService<ITotalsManager>();

        // Negative amounts only: we have not opened the output layer yet, but the
        // filter is left explicit — it also guards against re-reading our own
        // posting if hook order ever changes.
        var totalCost = 0m;
        foreach (var row in await totals.QueryMovementsAsync(
            "ItemCostFifo", $"[DocumentMetaId] = '{document.MetaId}'"))
        {
            var amount = row["Amount"] is null ? 0m : Convert.ToDecimal(row["Amount"]);
            if (amount < 0m) totalCost += -amount;
        }

        var movementDate = (full?.DocumentDate ?? document.DocumentDate) == default
            ? DateTime.UtcNow.Date
            : (full?.DocumentDate ?? document.DocumentDate).Date;
        var outputKey = new Dictionary<string, object?> { ["Item"] = product };
        await totals.PostMovementAsync("ItemCostFifo", document.MetaId, movementDate, outputKey,
            new Dictionary<string, decimal> { ["Quantity"] = outputQty, ["Amount"] = totalCost });

        // InventoryValue is sliced by the Item dynamic analytic — the same path
        // CostingIssueTotalDriver uses to mirror the issue (see that driver).
        var movements = context.GetService<IRegisterMovementService>();
        var inventoryValueId = (await context.GetService<IMetadataService>().GetAllRegistersAsync())
            .First(r => string.Equals(r.Name, "InventoryValue", StringComparison.OrdinalIgnoreCase)).MetaId;
        await movements.PostMovementAsync(inventoryValueId, document.MetaId, movementDate,
            new Dictionary<string, object?>(),
            new Dictionary<string, decimal> { ["Qty"] = outputQty, ["Value"] = totalCost },
            analytics: new Dictionary<string, object?> { ["Item"] = product });

        return EventResult.Ok();
    }

    private static Dictionary<Guid, decimal> ComponentDemand(IEnumerable<ProductionOrderComponentsTablePartRow> components)
    {
        var demand = new Dictionary<Guid, decimal>();
        foreach (var line in components)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.QtyRequired;
            demand[line.Component] = (demand.TryGetValue(line.Component, out var d) ? d : 0m) + qty;
        }
        return demand;
    }
}
