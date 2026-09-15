using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Services;

// Monthly posting calendar. The nuclear tenant-wide ClosedPeriod is a different
// door (IAccountingPeriodService). This service is the FiscalPeriod dictionary:
// which month covers a date, and whether that month still accepts movements.
public partial class FiscalPeriodService : IFiscalCalendar
{
    private readonly IDictionaryManager<FiscalPeriod> _periods;

    public FiscalPeriodService(IDictionaryManager<FiscalPeriod> periods)
        => _periods = periods;

    /// <summary>Period covering the date; null if the tenant has no calendar row for it.</summary>
    public async Task<Guid?> ResolveIdAsync(DateTime date)
        => (await ResolveRecordAsync(date))?.MetaId;

    /// <summary>
    /// Why a movement on this date must be refused, or null if allowed.
    /// No covering period is not a refusal — GL already skips, operations stay open.
    /// A covering period that is not Open is a refusal.
    /// </summary>
    public async Task<string?> ClosedReasonAsync(DateTime date)
    {
        var period = await ResolveRecordAsync(date);
        if (period == null) return null;
        if (IsOpen(period)) return null;
        return $"Учётный период «{period.Code}» закрыт "
             + $"({period.FromDate:yyyy-MM-dd} — {period.ToDate:yyyy-MM-dd}). "
             + "Проведение и распроведение в закрытый месяц запрещены.";
    }

    Task<string?> IFiscalCalendar.RefusePostingAsync(DateTime date, CancellationToken ct)
        => ClosedReasonAsync(date);

    private async Task<FiscalPeriod?> ResolveRecordAsync(DateTime date)
    {
        var d = date.Date;
        var matching = (await _periods.GetRecordsAsync())
            .Where(p => d >= p.FromDate.Date && d <= p.ToDate.Date)
            .ToList();

        if (matching.Count == 0) return null;
        if (matching.Count > 1)
            throw new InvalidOperationException(
                $"На {d:yyyy-MM-dd} приходится больше одного учётного периода (" +
                string.Join(", ", matching.Select(p => p.Code)) +
                "). Окна периодов пересекаться не должны.");

        return matching[0];
    }

    private static bool IsOpen(FiscalPeriod period)
        => string.Equals(period.Status, "Open", StringComparison.OrdinalIgnoreCase);
}
