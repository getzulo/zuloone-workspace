#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// One live campaign per (ItemGroup, CustomerType) covers a date. Empty
// ItemGroup is every line; empty CustomerType is every customer. A window
// that names a group or a type beats a wider one. EarnRate 0 means "no
// overlay" — the posting script then keeps LoyaltyTier / CRMSettings.
// Disabled rows are off the calendar so a paused promo does not block the
// next one. CustomerType is the same 8-character text as on the customer
// card, compared trimmed and case-insensitive.
public partial class LoyaltyCampaignService
{
    private readonly IDictionaryManager<LoyaltyCampaign> _rows;

    public LoyaltyCampaignService(IDictionaryManager<LoyaltyCampaign> rows)
        => _rows = rows;

    /// <summary>
    /// Campaign earn rate for this line and customer. A window that names
    /// the item group, the customer type, or both beats a wider one.
    /// 0 when nothing covers. Pass Guid.Empty and null for the open path.
    /// Null or blank customerType matches only campaigns that name no type:
    /// the phone catalogue has no customer.
    /// </summary>
    public async Task<decimal> EarnRateOfAsync(DateTime onDate, Guid itemGroup, string? customerType = null)
    {
        var winner = await WinnerAsync(onDate, itemGroup, customerType);
        return winner?.EarnRate ?? 0m;
    }

    /// <summary>
    /// The live window that wins, name included. Rate 0 and an empty name
    /// mean no overlay: the phone must not invent a tier rate, that one
    /// depends on the balance before the invoice. Omit customerType to see
    /// only campaigns that apply to every customer.
    /// </summary>
    public async Task<(decimal Rate, string Name)> OverlayOfAsync(
        DateTime onDate, Guid itemGroup, string? customerType = null)
    {
        var winner = await WinnerAsync(onDate, itemGroup, customerType);
        return winner == null ? (0m, "") : (winner.EarnRate, winner.Name ?? "");
    }

    private async Task<LoyaltyCampaign?> WinnerAsync(DateTime onDate, Guid itemGroup, string? customerType)
    {
        var wanted = Norm(customerType);
        return (await _rows.GetRecordsAsync("1 = 1"))
            .Where(r => !r.IsDisabled && Covers(r, onDate) && r.EarnRate > 0m)
            .Select(r => (Row: r, Score: Score(r, itemGroup, wanted)))
            .Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Row.EffectiveFrom)
            .Select(x => x.Row)
            .FirstOrDefault();
    }

    /// <summary>
    /// -1 does not apply. Otherwise 2 for a matching item group and 1 for a
    /// matching customer type: both beat either, either beats the open window.
    /// </summary>
    private static int Score(LoyaltyCampaign row, Guid itemGroup, string wantedType)
    {
        var rowType = Norm(row.CustomerType);
        var groupSpecific = row.ItemGroup != Guid.Empty;
        var typeSpecific = rowType.Length > 0;
        if (groupSpecific && row.ItemGroup != itemGroup) return -1;
        if (typeSpecific && rowType != wantedType) return -1;
        return (groupSpecific ? 2 : 0) + (typeSpecific ? 1 : 0);
    }

    private static string Norm(string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToUpperInvariant();

    /// <summary>
    /// MetaId of another live window that overlaps the same ItemGroup and
    /// the same customer type, or null. Empty type overlaps only empty type.
    /// "b2b" overlaps "B2B".
    /// </summary>
    public async Task<Guid?> FindOverlappingAsync(
        Guid exceptId, DateTime from, DateTime? to, Guid itemGroup, string? customerType = null)
    {
        var wanted = Norm(customerType);
        var others = await _rows.GetRecordsAsync("1 = 1");
        foreach (var row in others)
        {
            if (row.MetaId == exceptId || row.IsDisabled) continue;
            if (row.ItemGroup != itemGroup) continue;
            if (Norm(row.CustomerType) != wanted) continue;
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
