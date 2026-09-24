#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// One live campaign per ItemGroup covers a date. Empty ItemGroup is the
// global overlay. EarnRate 0 means "no overlay" — the posting script then
// keeps LoyaltyTier / CRMSettings. Disabled rows are off the calendar so a
// paused promo does not block the next one.
public partial class LoyaltyCampaignService
{
    private readonly IDictionaryManager<LoyaltyCampaign> _rows;

    public LoyaltyCampaignService(IDictionaryManager<LoyaltyCampaign> rows)
        => _rows = rows;

    /// <summary>
    /// Campaign earn rate for the item group: a matching specific window
    /// beats the global (empty ItemGroup) window. 0 when neither covers.
    /// Pass Guid.Empty for the global path only.
    /// </summary>
    public async Task<decimal> EarnRateOfAsync(DateTime onDate, Guid itemGroup)
    {
        var winner = await WinnerAsync(onDate, itemGroup);
        return winner?.EarnRate ?? 0m;
    }

    /// <summary>
    /// The live window that wins for this group, name included. A matching
    /// group beats the global window. Rate 0 and an empty name mean no overlay:
    /// the phone must not invent a tier rate, that one depends on the balance
    /// before the invoice.
    /// </summary>
    public async Task<(decimal Rate, string Name)> OverlayOfAsync(DateTime onDate, Guid itemGroup)
    {
        var winner = await WinnerAsync(onDate, itemGroup);
        return winner == null ? (0m, "") : (winner.EarnRate, winner.Name ?? "");
    }

    private async Task<LoyaltyCampaign?> WinnerAsync(DateTime onDate, Guid itemGroup)
    {
        var live = (await _rows.GetRecordsAsync("1 = 1"))
            .Where(r => !r.IsDisabled && Covers(r, onDate) && r.EarnRate > 0m)
            .ToList();
        if (itemGroup != Guid.Empty)
        {
            var specific = live
                .Where(r => r.ItemGroup == itemGroup)
                .OrderByDescending(r => r.EffectiveFrom)
                .FirstOrDefault();
            if (specific != null) return specific;
        }

        return live
            .Where(r => r.ItemGroup == Guid.Empty)
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefault();
    }

    /// <summary>MetaId of another live window that overlaps the same ItemGroup, or null.</summary>
    public async Task<Guid?> FindOverlappingAsync(
        Guid exceptId, DateTime from, DateTime? to, Guid itemGroup)
    {
        var others = await _rows.GetRecordsAsync("1 = 1");
        foreach (var row in others)
        {
            if (row.MetaId == exceptId || row.IsDisabled) continue;
            if (row.ItemGroup != itemGroup) continue;
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
