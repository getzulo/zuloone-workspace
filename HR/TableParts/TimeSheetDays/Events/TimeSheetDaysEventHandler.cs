#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class TimeSheetDaysEventHandler : TypedTablePartEventHandler<TimeSheetDaysTablePartRow>
{
    public override async Task<EventResult> OnFieldChangedAsync(
        TimeSheetDaysTablePartRow row, string fieldName, object? value, EventContext context)
    {
        var prior = await next(row, fieldName, value, context);
        if (!prior.Success) return prior;

        var hoursPerDay = await context.GetService<ITimeSheetService>().HoursPerDayAsync();

        if (string.Equals(fieldName, nameof(row.IsAbsent), StringComparison.OrdinalIgnoreCase))
        {
            row.IsManual = true;
            if (row.IsAbsent is true)
            {
                row.Hours = 0m;
                if (row.DayKind == AttendanceDayKind.Work)
                    row.DayKind = AttendanceDayKind.Absence;
            }
            else
            {
                row.DayKind = AttendanceDayKind.Work;
                if ((row.Hours ?? 0m) <= 0m)
                    row.Hours = hoursPerDay;
            }
        }
        else if (string.Equals(fieldName, nameof(row.DayKind), StringComparison.OrdinalIgnoreCase))
        {
            row.IsManual = true;
            if (row.DayKind == AttendanceDayKind.Work)
            {
                row.IsAbsent = false;
                if ((row.Hours ?? 0m) <= 0m)
                    row.Hours = hoursPerDay;
            }
            else
            {
                row.IsAbsent = true;
                row.Hours = 0m;
            }
        }
        else if (string.Equals(fieldName, nameof(row.Hours), StringComparison.OrdinalIgnoreCase))
        {
            row.IsManual = true;
            var hours = row.Hours ?? 0m;
            if (hours > 0m)
            {
                row.IsAbsent = false;
                row.DayKind = AttendanceDayKind.Work;
            }
            else if (row.DayKind == AttendanceDayKind.Work)
            {
                row.IsAbsent = true;
                row.DayKind = AttendanceDayKind.Absence;
            }
        }

        var header = Owner<TimeSheet>(context);
        if (header != null)
            RollupHours(header);

        return EventResult.Ok();
    }

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
            else sheet.Lines.RemoveAt(i);
        }
        foreach (var pair in byEmp)
        {
            if (seen.Contains(pair.Key)) continue;
            sheet.Lines.Add(new TimeSheetLinesTablePartRow { Employee = pair.Key, Hours = pair.Value });
        }
    }
}
