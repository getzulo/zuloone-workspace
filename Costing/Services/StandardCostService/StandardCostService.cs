#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Dated standard of an item. Windows may have gaps; two open intervals
// of the same item may not overlap. Missing cover is a honest zero —
// there is no fact to price against, same as a surplus with no lots.
public partial class StandardCostService
{
    private readonly IDictionaryManager<StandardCost> _rows;

    public StandardCostService(IDictionaryManager<StandardCost> rows)
        => _rows = rows;

    /// <summary>Unit standard covering the date, or 0 when none.</summary>
    public async Task<decimal> OfAsync(Guid item, DateTime onDate)
    {
        if (item == Guid.Empty) return 0m;
        var hit = await CoveringAsync(item, onDate);
        return hit?.Amount ?? 0m;
    }

    /// <summary>qty × standard − actual. Positive: cheaper than the plan.</summary>
    public async Task<decimal> VarianceAsync(
        Guid item, decimal qty, decimal actualAmount, DateTime onDate)
    {
        var standard = await OfAsync(item, onDate);
        return Math.Round(qty * standard - actualAmount, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>MetaId of another window that overlaps, or null.</summary>
    public async Task<Guid?> FindOverlappingAsync(
        Guid item, Guid exceptId, DateTime from, DateTime? to)
    {
        if (item == Guid.Empty) return null;
        var others = await _rows.GetRecordsAsync($"Item = '{item}'");
        foreach (var row in others)
        {
            if (row.MetaId == exceptId) continue;
            if (Overlaps(from, to, row.EffectiveFrom, row.EffectiveTo))
                return row.MetaId;
        }
        return null;
    }

    private async Task<StandardCost?> CoveringAsync(Guid item, DateTime onDate)
    {
        var rows = await _rows.GetRecordsAsync($"Item = '{item}'");
        return rows
            .Where(r => Covers(r, onDate))
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefault();
    }

    private static bool Covers(StandardCost row, DateTime onDate)
        => row.EffectiveFrom <= onDate
           && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value >= onDate);

    private static bool Overlaps(DateTime aFrom, DateTime? aTo, DateTime bFrom, DateTime? bTo)
    {
        var aEnd = aTo ?? DateTime.MaxValue;
        var bEnd = bTo ?? DateTime.MaxValue;
        return aFrom <= bEnd && bFrom <= aEnd;
    }
}
