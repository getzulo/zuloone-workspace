#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// One live campaign covers a date. EarnRate 0 means "no overlay" — the
// posting script then keeps LoyaltyTier / CRMSettings. Disabled rows are
// off the calendar so a paused promo does not block the next one.
public partial class LoyaltyCampaignService
{
    private readonly IDictionaryManager<LoyaltyCampaign> _rows;

    public LoyaltyCampaignService(IDictionaryManager<LoyaltyCampaign> rows)
        => _rows = rows;

    /// <summary>Campaign earn rate covering the date, or 0 when none.</summary>
    public async Task<decimal> EarnRateOfAsync(DateTime onDate)
    {
        var hit = (await _rows.GetRecordsAsync("1 = 1"))
            .Where(r => !r.IsDisabled && Covers(r, onDate) && r.EarnRate > 0m)
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefault();
        return hit?.EarnRate ?? 0m;
    }

    /// <summary>MetaId of another live window that overlaps, or null.</summary>
    public async Task<Guid?> FindOverlappingAsync(Guid exceptId, DateTime from, DateTime? to)
    {
        var others = await _rows.GetRecordsAsync("1 = 1");
        foreach (var row in others)
        {
            if (row.MetaId == exceptId || row.IsDisabled) continue;
            if (Overlaps(from, to, row.EffectiveFrom, row.EffectiveTo))
                return row.MetaId;
        }
        return null;
    }

    private static bool Covers(LoyaltyCampaign row, DateTime onDate)
        => row.EffectiveFrom <= onDate
           && (!row.EffectiveTo.HasValue || row.EffectiveTo.Value >= onDate);

    private static bool Overlaps(DateTime aFrom, DateTime? aTo, DateTime bFrom, DateTime? bTo)
    {
        var aEnd = aTo ?? DateTime.MaxValue;
        var bEnd = bTo ?? DateTime.MaxValue;
        return aFrom <= bEnd && bFrom <= aEnd;
    }
}
