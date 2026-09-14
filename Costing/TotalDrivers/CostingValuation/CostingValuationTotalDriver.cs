#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Totals;
using ZuloOne.Totals.Calculation;

// ═══ DISPOSAL-VALUATION DRIVER for the ItemCostFifo register ═════════════════
//
// The register stores receipt layers (lots); the engine calls CalculateOutcomes
// to PLAN which layers and at which price to take the issue from. The method
// comes from the CostingSettings singleton dictionary and is NOT hard-coded:
//
//   CostingMethod = FIFO (default, including when there is no settings record) —
//     FifoTotalDriver base: the issue consumes the oldest lots, lot cost
//     moves into disposal as-is.
//
//   CostingMethod = AVG — weighted average: a unit is valued as
//     Σ amounts of open lots / Σ their quantities, and quantity is taken
//     from lots PROPORTIONALLY to their remaining balances. The proportion
//     is not decoration here, it is a convergence condition: the physical
//     layer stores the ORIGINAL lot price, and its outstanding cost is
//     always Amount/OriginalQty×RemainingQty (the platform only consumes
//     RemainingQty). Consume lots by age and value at average — the sum of
//     outstanding layer costs will drift from the register balance by
//     exactly the method difference. With proportional consumption all lots
//     shrink by the same share, and both quantities stay equal.
//
//   RoundCosts — round disposal cost to 2 places (the same
//     CalculatePartialAmount hook as stand TBRounding).
//
// WHERE settings are read. In the FIELD INITIALIZER, i.e. the constructor,
// i.e. at ITotalDriverProvider.ResolveAsync — the only moment in the
// driver's life when the register connection is NOT yet open
// (RegisterMovementService resolves the driver one line before
// connection.OpenAsync). All calculation hooks — LoadTotalState,
// CalculateOutcomes — the platform already calls with the connection
// open: a second connection inside the same surrounding transaction would
// promote it to distributed, which the stand server does not support. The
// driver instance lives for one movement, so «once in the constructor» is
// exactly «fresh settings on every movement».
public partial class CostingValuationTotalDriver
{
    private readonly (bool Average, bool Round) _settings = ReadSettings();

    /// <summary>Method and rounding from CostingSettings; no record — FIFO without rounding.</summary>
    private static (bool Average, bool Round) ReadSettings()
    {
        var rows = GetService<IDictionaryManager>()
            .GetRecordsAsync<CostingSettings>(null, 1).GetAwaiter().GetResult();
        if (rows.Count == 0) return (false, false);
        return (string.Equals(rows[0].CostingMethod, "AVG", StringComparison.OrdinalIgnoreCase), rows[0].RoundCosts);
    }

    private decimal Round(decimal value)
        => _settings.Round ? Math.Round(value, 2, MidpointRounding.AwayFromZero) : value;

    /// <summary>FIFO branch: partial lot cost with rounding per the setting.</summary>
    protected override decimal CalculatePartialAmount(decimal lotQuantity, decimal lotAmount, decimal transQuantity)
        => Round(base.CalculatePartialAmount(lotQuantity, lotAmount, transQuantity));

    public override ICollection<DetailedTransactionValue> CalculateOutcomes(
        TransactionValue tv, IEnumerable<TransactionValue> incomes)
    {
        if (!_settings.Average) return base.CalculateOutcomes(tv, incomes);

        var need = -tv.GetValue(QuantityVariableName);
        if (need <= 0m) return base.CalculateOutcomes(tv, incomes);

        var key = GetKeyValue(tv);
        var queue = FindQueue(key);
        var lots = queue == null ? new List<DetailedTransactionValue>() : queue.ToList();
        var haveQty = lots.Sum(l => l.GetValue(QuantityVariableName));

        // Layer shortage is not the valuation method's job. Hand it to the base:
        // it will form an «issue below zero» lot, and the engine will reject
        // over-issue, same as FIFO.
        if (haveQty < need) return base.CalculateOutcomes(tv, incomes);

        var haveAmount = lots.Sum(l => l.GetValue(AmountVariableName));
        var unit = haveAmount / haveQty;
        var totalCost = Round(unit * need);

        // Shares: lot remainder × need / total. The last lot gets the
        // REMAINDER of the need — so the sum of shares converges to the
        // whole even on division «tails».
        var takes = new decimal[lots.Count];
        var left = need;
        for (var i = 0; i < lots.Count && left > 0m; i++)
        {
            var lotQty = lots[i].GetValue(QuantityVariableName);
            var share = i == lots.Count - 1 ? left : lotQty * need / haveQty;
            if (share > lotQty) share = lotQty;
            if (share > left) share = left;
            takes[i] = share;
            left -= share;
        }
        // After clamps, leftover is spread across lots that still have room.
        for (var i = 0; i < lots.Count && left > 0m; i++)
        {
            var room = lots[i].GetValue(QuantityVariableName) - takes[i];
            if (room <= 0m) continue;
            var add = room < left ? room : left;
            takes[i] += add;
            left -= add;
        }

        var outcomes = new List<DetailedTransactionValue>();
        var costLeft = totalCost;
        var lastIndex = Array.FindLastIndex(takes, t => t > 0m);
        var survivors = new Queue<DetailedTransactionValue>();
        for (var i = 0; i < lots.Count; i++)
        {
            var lot = lots[i];
            var lotQty = lot.GetValue(QuantityVariableName);
            var lotAmount = lot.GetValue(AmountVariableName);
            var take = takes[i];
            if (take > 0m)
            {
                // The last share gets the leftover cost: otherwise rounding
                // each share separately would pull the sum away from totalCost.
                var cost = i == lastIndex ? costLeft : Round(unit * take);
                costLeft -= cost;

                var detail = tv.CreateDetailedTransactionValue();
                detail.SetValue(QuantityVariableName, -take);
                detail.SetValue(AmountVariableName, -cost);
                detail.LotNo = lot.LotNo;
                detail.DeltaSubNo = GetNextDeltaSubNo(detail.DeltaNo);
                // The lot carries non-operational dimensions itself (as in base FIFO).
                foreach (var name in detail.TotalDescriptor.Dimensions.Where(d => !d.IsOperational).Select(d => d.Name))
                    detail.SetCoordinate(name, lot.GetCoordinate(name));
                outcomes.Add(detail);

                lot.SetValue(QuantityVariableName, lotQty - take);
                lot.SetValue(AmountVariableName, lotAmount - cost);
            }
            if (lot.GetValue(QuantityVariableName) > 0m) survivors.Enqueue(lot);
        }

        // The lot queue is rebuilt: consumption ran across all lots at once,
        // and RemoveFifoLot can only take from the head.
        if (survivors.Count > 0) QueueIndex[key] = survivors;
        else QueueIndex.Remove(key);

        return outcomes;
    }
}
