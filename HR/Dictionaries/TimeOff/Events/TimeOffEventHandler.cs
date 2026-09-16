#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

public partial class TimeOffEventHandler : TypedDictionaryEventHandler<TimeOff>
{
    public override async Task<EventResult> OnBeforeSaveAsync(TimeOff record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.Kind == AttendanceDayKind.Work)
            return EventResult.Cancel("Укажите вид отсутствия: отпуск, больничный или прогул.");

        var from = DayOf(record.DateFrom);
        var to = DayOf(record.DateTo);
        if (from == null || to == null || from > to)
            return EventResult.Cancel("Дата начала не может быть позже даты окончания.");

        if (record.Employee == Guid.Empty)
            return EventResult.Cancel("Укажите сотрудника.");

        var others = await context.GetService<IDictionaryManager<TimeOff>>().GetRecordsAsync("1 = 1");
        var overlap = others.Any(row =>
            row.MetaId != record.MetaId
            && row.Employee == record.Employee
            && DayOf(row.DateFrom) <= to
            && from <= DayOf(row.DateTo));
        if (overlap)
            return EventResult.Cancel("У сотрудника уже есть отсутствие на эти даты.");

        if (string.IsNullOrWhiteSpace(record.Name))
            record.Name = $"{record.Kind} {from:yyyy-MM-dd}–{to:yyyy-MM-dd}";

        return EventResult.Ok();
    }

    private static DateTime? DayOf(object? value)
        => value is DateTime d && d != default ? d.Date : null;
}
