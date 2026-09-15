#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

// ═══ COST OF AN UNCOMPENSATED RECEIPT ═══════════════════════════════════════
//
// The CostingIssue driver covers only the ISSUE side: warehouse on-hand went
// down — cost was written off. It deliberately does not touch the receipt
// side, because a positive net has different natures: a purchase order opens
// a lot itself (ReceiptFifoTx), a production output opens one itself
// (ProductionOrderEventHandler), while surplus and a recount-up opened one
// for NOBODY. Goods appeared in the warehouse without a lot and were then
// silently written off at zero: Math.Min(-net, onHand) in the driver took
// zero on-hand in lots, and inventory value did not decrease at all.
//
// WHAT to value the surplus at. It has no purchase price by definition —
// the goods were not bought, they were found. The item's CURRENT AVERAGE
// is taken (Amount/Quantity of open lots): found units of the same item
// cost as much as those already on the shelf. The average does not change
// from this — a receipt at average preserves it, and inventory valuation
// grows by exactly the cost of what was found.
//
// No lots at all (the item was never purchased) — there is no price and
// nowhere for one to come from, so the lot is opened at zero. That is an
// honest zero, not lost cost: the system has no fact about what this item
// is worth.
//
// WHY FROM MOVEMENTS, NOT FROM DOCUMENT LINES. Adjustment and stock-count
// lines differ (Quantity vs CountedQty, and the latter also has a delta to
// on-hand), while Stock movements are the same and already normalized to
// the base unit. Computing net from the document's movements, the service
// works the same for both and will survive any third document of this kind.
public partial class SurplusCostingService
{
    private readonly ITotalsManager _totals;
    private readonly IRegisterMovementService _movements;
    private readonly IMetadataService _metadata;

    public SurplusCostingService(
        ITotalsManager totals,
        IRegisterMovementService movements,
        IMetadataService metadata)
    {
        _totals = totals;
        _movements = movements;
        _metadata = metadata;
    }

    /// <summary>
    /// Open cost lots for everything the document added to the warehouse
    /// beyond what it wrote off. Returns the captured cost (0 — nothing to receive).
    /// </summary>
    public async Task<decimal> CaptureSurplusAsync(Guid documentMetaId, DateTime movementDate)
    {
        // Net by item within the document: a transfer (−from cell, +to
        // cell) yields zero and opens no lots, just as the driver writes none off.
        var net = new Dictionary<Guid, decimal>();
        foreach (var row in await _totals.QueryMovementsAsync("Stock", $"[DocumentMetaId] = '{documentMetaId}'"))
        {
            if (row["Item"] is null) continue;
            var item = (Guid)row["Item"]!;
            net[item] = (net.TryGetValue(item, out var acc) ? acc : 0m) + Convert.ToDecimal(row["Qty"]);
        }

        var surplus = net.Where(kv => kv.Value > 0m).ToList();
        if (surplus.Count == 0) return 0m;

        var inventoryValueId = (await _metadata.GetAllRegistersAsync())
            .First(r => string.Equals(r.Name, "InventoryValue", StringComparison.OrdinalIgnoreCase)).MetaId;

        var captured = 0m;
        foreach (var (item, qty) in surplus)
        {
            var key = new Dictionary<string, object?> { ["Item"] = item };

            var onHand = await _totals.GetBalanceAsync("ItemCostFifo", "Quantity", key);
            var value = await _totals.GetBalanceAsync("ItemCostFifo", "Amount", key);
            var unitCost = onHand > 0m ? value / onHand : 0m;
            var amount = Math.Round(unitCost * qty, 2, MidpointRounding.AwayFromZero);

            await _totals.PostMovementAsync("ItemCostFifo", documentMetaId, movementDate, key,
                new Dictionary<string, decimal> { ["Quantity"] = qty, ["Amount"] = amount });

            // InventoryValue is sliced by a DYNAMIC Item analytic, and
            // ITotalsManager.PostMovementAsync does not accept analytics — this
            // posting goes through the register engine, same as the write-off driver.
            await _movements.PostMovementAsync(
                inventoryValueId, documentMetaId, movementDate,
                new Dictionary<string, object?>(),
                new Dictionary<string, decimal> { ["Qty"] = qty, ["Value"] = amount },
                analytics: new Dictionary<string, object?> { ["Item"] = item });

            captured += amount;
        }

        return captured;
    }
}
