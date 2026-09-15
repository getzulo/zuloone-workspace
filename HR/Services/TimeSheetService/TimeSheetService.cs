using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;

// Timesheet attendance: a standard Mon–Fri week (40 hours by default) for every
// employee of the division, then TimeOff windows (vacation / sick / absence)
// punch holes in that week. Manual day ticks survive a refill.
//
// Managers are resolved at call time: a captured scoped IDictionaryManager
// dies before the command finishes (same as TradeProfileService).
public partial class TimeSheetService
{
    private static IDictionaryManager Live => ScriptServices.Get<IDictionaryManager>();
    private static IDocumentManager Docs => ScriptServices.Get<IDocumentManager>();

    /// <summary>Hours of a standard working day. WeeklyHours / 5, else WorkHoursPerDay, else 8 (40h week).</summary>
    public async Task<decimal> HoursPerDayAsync()
    {
        var s = (await Live.GetRecordsAsync<HRSettings>("1 = 1", take: 1)).FirstOrDefault();
        if (s is null) return 8m;
        if (s.WeeklyHours > 0m) return Math.Round(s.WeeklyHours / 5m, 2, MidpointRounding.AwayFromZero);
        if (s.WorkHoursPerDay > 0m) return s.WorkHoursPerDay;
        return 8m;
    }

    /// <summary>Rebuilds auto days for the period and rolls hours into Lines. Manual days stay.</summary>
    public async Task<int> FillStandardAsync(Guid sheetId)
    {
        var sheet = await Docs.GetDocumentAsync<TimeSheet>(sheetId);
        if (sheet == null)
            throw new InvalidOperationException("Табель не найден.");
        if (sheet.Division == Guid.Empty)
            throw new InvalidOperationException("Укажите подразделение.");
        var from = DayOf(sheet.PeriodFrom);
        var to = DayOf(sheet.PeriodTo);
        if (from == null || to == null)
            throw new InvalidOperationException("Укажите период табеля.");
        if (from > to)
            throw new InvalidOperationException("Дата начала периода не может быть позже даты окончания.");

        var hours = await HoursPerDayAsync();
        var employees = (await Live.GetRecordsAsync<Employee>($"Division = '{sheet.Division}'")).ToList();
        var timeOff = (await Live.GetRecordsAsync<TimeOff>("1 = 1")).ToList();

        for (var i = sheet.Days.Count - 1; i >= 0; i--)
        {
            if (sheet.Days[i].IsManual != true)
                sheet.Days.RemoveAt(i);
        }

        var manualKeys = new HashSet<(Guid Employee, DateTime Day)>();
        foreach (var d in sheet.Days)
        {
            var work = DayOf(d.WorkDate);
            if (work != null) manualKeys.Add((d.Employee, work.Value));
        }

        var added = 0;
        foreach (var emp in employees)
        {
            var hire = DayOf(emp.HireDate) ?? DateTime.MinValue;
            for (var day = from.Value; day <= to.Value; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                if (hire > day) continue;
                if (manualKeys.Contains((emp.MetaId, day))) continue;

                var off = timeOff.FirstOrDefault(t =>
                    t.Employee == emp.MetaId
                    && t.Kind != AttendanceDayKind.Work
                    && DayOf(t.DateFrom) <= day
                    && day <= DayOf(t.DateTo));

                sheet.Days.Add(new TimeSheetDaysTablePartRow
                {
                    Employee = emp.MetaId,
                    WorkDate = day,
                    DayKind = off?.Kind ?? AttendanceDayKind.Work,
                    IsAbsent = off != null,
                    Hours = off == null ? hours : 0m,
                    IsManual = false,
                });
                added++;
            }
        }

        RollupHours(sheet);
        await Docs.SaveDocumentAsync(sheet);
        return added;
    }

    private static DateTime? DayOf(object? value)
        => value is DateTime d && d != default ? d.Date : null;

    private static void RollupHours(TimeSheet sheet)
    {
            if (sheet.Days.Count == 0) return;

            var byEmp = sheet.Days
                .Where(d => d.Employee != Guid.Empty)
                .GroupBy(d => d.Employee)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Hours));

            var seen = new HashSet<Guid>();
            for (var i = sheet.Lines.Count - 1; i >= 0; i--)
            {
                var emp = sheet.Lines[i].Employee;
                if (byEmp.TryGetValue(emp, out var hours))
                {
                    sheet.Lines[i].Hours = hours;
                    seen.Add(emp);
                }
                else
                {
                    sheet.Lines.RemoveAt(i);
                }
            }

        foreach (var pair in byEmp)
        {
            if (seen.Contains(pair.Key)) continue;
            sheet.Lines.Add(new TimeSheetLinesTablePartRow { Employee = pair.Key, Hours = pair.Value });
        }
    }
}
