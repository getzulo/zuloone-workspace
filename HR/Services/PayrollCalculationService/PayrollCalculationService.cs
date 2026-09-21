#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// One formula for AccruePayroll. MonthlySalary > 0 is the salaried scheme:
// a full standard month (weekdays × HoursPerDay) pays the oklad; fewer
// hours (time off already punched in the sheet) prorate. Unset monthly
// keeps hours × HourlyRate. No IsMonthly flag — 0 is hourly.
//
// ITimeSheetService is resolved at call time: another script IFoo cannot
// be constructor-injected (MS.DI never registered it).
public partial class PayrollCalculationService
{
    private readonly IDictionaryManager<Position> _positions;

    public PayrollCalculationService(IDictionaryManager<Position> positions)
        => _positions = positions;

    private static ITimeSheetService Sheets => ScriptServices.Get<ITimeSheetService>();

    /// <summary>Pay for the hours in the timesheet window.</summary>
    public async Task<decimal> AmountOfAsync(
        Guid positionId, decimal hours, DateTime periodFrom, DateTime periodTo)
    {
        if (positionId == Guid.Empty || hours <= 0m) return 0m;
        var pos = await _positions.GetRecordAsync(positionId);
        if (pos == null) return 0m;
        if (pos.MonthlySalary > 0m)
        {
            var standard = await StandardHoursAsync(periodFrom, periodTo);
            if (standard <= 0m) return 0m;
            return Math.Round(pos.MonthlySalary * hours / standard, 2, MidpointRounding.AwayFromZero);
        }
        return Math.Round(hours * pos.HourlyRate, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Weekdays in [from, to] × standard day length.</summary>
    public async Task<decimal> StandardHoursAsync(DateTime periodFrom, DateTime periodTo)
    {
        var from = periodFrom.Date;
        var to = periodTo.Date;
        if (to < from) return 0m;
        var day = await Sheets.HoursPerDayAsync();
        var weekdays = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            weekdays++;
        }
        return weekdays * day;
    }
}
