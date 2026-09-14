#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// FISCAL-PERIOD CALENDAR INTEGRITY.
//
// A period is a date window that reporting is collected for, and a posting
// picks it BY DATE. Hence two requirements metadata cannot express:
//
//  1. Any date falls into EXACTLY ONE period. If two overlap, resolution
//     returns a random one: some postings go to one month, some to another, and
//     that only shows up at reconciliation. The refusal sits on INPUT, where
//     the person sees neighbouring rows. GeneralLedgerService also refuses when
//     two periods match — but it learns that at posting time, so an operation
//     would catch a master-data error.
//
//  2. Periods close IN ORDER. "February closed, January open" is not a
//     calendar, it is a hole: the platform posting ban is expressed as ONE
//     cutoff date, and that state does not map onto it. Allowing it would
//     guaranteed-drift from the platform.
public partial class FiscalPeriodEventHandler : TypedDictionaryEventHandler<FiscalPeriod>
{
    /// <summary>The only status meaning "the period accepts postings".
    /// The set is closed and belongs in metadata as an enum; while it is a
    /// string — comparison is case-insensitive, and anything unknown is treated
    /// as CLOSED: a typo in the status must forbid posting, not allow it.</summary>
    private const string OpenStatus = "Open";

    private static bool IsOpen(FiscalPeriod period)
        => string.Equals(period.Status, OpenStatus, StringComparison.OrdinalIgnoreCase);

    public override async Task<EventResult> OnBeforeSaveAsync(FiscalPeriod record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.FromDate > record.ToDate)
            return EventResult.Cancel("Начало периода должно быть не позже конца");

        var periods = context.GetService<IDictionaryManager<FiscalPeriod>>();
        var siblings = (await periods.GetRecordsAsync())
            .Where(p => p.MetaId != record.MetaId)
            .ToList();

        var clash = siblings.FirstOrDefault(p =>
            record.FromDate.Date <= p.ToDate.Date && p.FromDate.Date <= record.ToDate.Date);
        if (clash != null)
            return EventResult.Cancel(
                $"Окно периода пересекается с периодом «{clash.Code}» "
                + $"({clash.FromDate:yyyy-MM-dd} — {clash.ToDate:yyyy-MM-dd}). "
                + "На каждую дату должен приходиться ровно один период.");

        // Order is checked only on CLOSE: an already closed period can be
        // re-saved (code or caption edit) without hitting the calendar.
        if (!IsOpen(record))
        {
            var earlierOpen = siblings
                .Where(p => p.ToDate.Date < record.FromDate.Date && IsOpen(p))
                .OrderByDescending(p => p.ToDate)
                .FirstOrDefault();

            if (earlierOpen != null)
                return EventResult.Cancel(
                    $"Нельзя закрыть период: более ранний период «{earlierOpen.Code}» "
                    + $"({earlierOpen.FromDate:yyyy-MM-dd} — {earlierOpen.ToDate:yyyy-MM-dd}) "
                    + "ещё открыт. Периоды закрываются по порядку.");
        }

        return EventResult.Ok();
    }
}
