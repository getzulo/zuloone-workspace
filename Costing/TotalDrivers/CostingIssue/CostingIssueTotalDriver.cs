#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
// ZuloOne.Managers and ZuloOne.Totals are not opened wholesale: the names
// TransactionCollection / TransactionPairCollection / ITotalsManager exist
// both in the document-posting space and in the totals space.
using ITotalsManager = ZuloOne.Managers.ITotalsManager;
using TransactionCollection = ZuloOne.Totals.TransactionCollection;
using TransactionPairCollection = ZuloOne.Totals.TransactionPairCollection;

// ═══ COST-ISSUE DRIVER ══════════════════════════════════════════════════════
//
// The cost issue leg is produced NOT by the document, but by a warehouse
// movement: the driver hangs on the Stock register, sees the FULL set of
// postings of the document being posted (the platform calls
// ValidateTransactions on every driver in the chain, handing it the full
// set), and then — after the movements are written — writes off the cost of
// the issued quantity.
//
// WHY NOT POSTINGS ON THE DOCUMENTS. Cost must be written off by a sale,
// a write-off, a production issue, a warehouse pick, and any future issue
// document. A leg in every transactional script is N copies of one rule
// that drift: it is enough to create a document and forget about cost.
// The rule is one and lives in one place: WAREHOUSE ON-HAND WENT DOWN —
// cost was written off. Because of this the Costing model creates neither
// documents, nor postings, nor services: it has registers, a setting, and
// two drivers.
//
// NET quantity by item, not individual postings. A transfer between cells
// is TWO Stock movements on one item (−24 from FromCell, +24 to ToCell).
// The item did not leave: it moved. If the driver treated every negative
// posting as a disposal — a transfer would write off cost, and inventory
// valuation would drop when a box is moved from shelf to shelf. So
// movements are COLLAPSED by item within the document, and only a net
// minus is written off. A production order (−components, +finished good)
// collapses by DIFFERENT items and therefore writes off exactly the
// components.
//
// QUANTITY IS ALREADY NORMALIZED. All transactional scripts write
// BaseQuantity (the item's base unit) to Stock — the driver reads the
// register, not document lines, and therefore cannot confuse «5 boxes»
// with «60 pieces» in principle. Cost layers were opened by the same
// receipt in the same base unit.
//
// HOW MUCH to write off. Cost exists only for the quantity whose receipt
// recorded it: ItemCostFifo lots are created by receiving a purchase
// order. Issuing quantity that is not in the lots (test balances opened
// as direct register movements, past stock-count surpluses, production
// output) has nothing to write off against — the minimum of issued and
// on-hand in lots is taken. Otherwise the engine would reject layer
// over-issue and fail a document that has nothing to do with costing.
//
// WHAT to value at is not this driver's decision: the ItemCostFifo
// register is computed by its own CostingValuation driver, and the method
// (FIFO/AVG) with rounding is taken there from CostingSettings. Here the
// FACT is taken: the amount by which the engine reduced the lots, and
// exactly that is written off from inventory value. So the two quantities
// cannot drift by construction — whichever method is chosen in settings.
public partial class CostingIssueTotalDriver
{
    private const string StockQuantity = "Qty";
    private const string StockItem = "Item";

    // Net movement by item for the document: minus — disposal, plus/zero — none.
    private readonly Dictionary<Guid, decimal> _netByItem = new();

    /// <summary>
    /// The platform hands the FULL set of the document's postings here — all
    /// registers in the chain. We take only our own and sum by item. We look
    /// at single postings, not pairs: Stock is declared as a SINGLE-entry
    /// register (balance = actual on-hand, there is no counterpart «External»
    /// leg), and a paired posting is not accepted on it by the platform at all.
    /// </summary>
    public override void ValidateTransactions(
        TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        base.ValidateTransactions(transactionPairs, transactions);

        foreach (var tv in transactions)
        {
            if (tv.TotalDescriptor.Guid != TotalID) continue;
            if (tv.IsCoordinateNull(StockItem) || tv.IsValueNull(StockQuantity)) continue;
            var item = tv.GetCoordinate(StockItem);
            _netByItem[item] = (_netByItem.TryGetValue(item, out var acc) ? acc : 0m) + tv.GetValue(StockQuantity);
        }
    }

    /// <summary>
    /// Warehouse movements are already written — write off the cost of what left.
    /// The hook is synchronous, and the write-off goes through managers: this
    /// is the only point in the driver's life AFTER movements are written, and
    /// the register connection is already gone — a DB call from here does not
    /// turn the surrounding transaction into a distributed one.
    /// </summary>
    public override void EndDocument(DateTime transactionDate, Guid docId)
    {
        base.EndDocument(transactionDate, docId);

        var issues = _netByItem.Where(kv => kv.Value < 0m).ToList();
        // The driver instance lives for one posting, but we clear explicitly:
        // EndDocument is a public hook, and a second call must not write off
        // cost a second time.
        _netByItem.Clear();
        if (issues.Count == 0) return;

        WriteOffAsync(issues, transactionDate, docId).GetAwaiter().GetResult();
    }

    private async Task WriteOffAsync(
        List<KeyValuePair<Guid, decimal>> issues, DateTime movementDate, Guid docId)
    {
        var totals = GetService<ITotalsManager>();

        // InventoryValue is sliced by a DYNAMIC Item analytic (required), and
        // ITotalsManager.PostMovementAsync does not accept analytics — a movement
        // without it will be rejected by the register. So this posting goes
        // through the register engine, the only one whose signature carries
        // them. (A hole in the manager contract, not in discipline: closing
        // it is a platform change.)
        var movements = GetService<IRegisterMovementService>();
        var inventoryValueId = (await GetService<IMetadataService>().GetAllRegistersAsync())
            .First(r => string.Equals(r.Name, "InventoryValue", StringComparison.OrdinalIgnoreCase)).MetaId;

        foreach (var (item, net) in issues)
        {
            var key = new Dictionary<string, object?> { ["Item"] = item };

            var onHand = await totals.GetBalanceAsync("ItemCostFifo", "Quantity", key);
            var take = Math.Min(-net, onHand);
            if (take <= 0m) continue;

            var valueBefore = await totals.GetBalanceAsync("ItemCostFifo", "Amount", key);

            // Lot issue: the engine WILL REPLACE Amount with the cost that
            // the register driver computes (FIFO or AVG — per the setting),
            // so here it is zero.
            await totals.PostMovementAsync("ItemCostFifo", docId, movementDate, key,
                new Dictionary<string, decimal> { ["Quantity"] = -take, ["Amount"] = 0m });

            var cost = valueBefore - await totals.GetBalanceAsync("ItemCostFifo", "Amount", key);

            await movements.PostMovementAsync(
                inventoryValueId, docId, movementDate,
                new Dictionary<string, object?>(),
                new Dictionary<string, decimal> { ["Qty"] = -take, ["Value"] = -cost },
                analytics: new Dictionary<string, object?> { ["Item"] = item });
        }
    }
}
