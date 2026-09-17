#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for TimeSheet documents.
// `header` is a typed TimeSheet entity — access fields directly (header.Number).
// Record events (insert/update/delete/validate) plus the document-only posting
// events are stubbed below. Cancel a transition with EventResult.Cancel("reason");
// document table-part rows are available via document.TableParts["Name"].
public partial class TimeSheetEventHandler : TypedDocumentEventHandler<TimeSheet>
{
    // Building a new document server-side: seed header defaults (number, date).
    public override async Task<EventResult> OnBeforeCreateAsync(TimeSheet header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        if (DayOf(header.PeriodFrom) == null && DayOf(header.PeriodTo) == null)
        {
            var today = DateTime.UtcNow.Date;
            var start = new DateTime(today.Year, today.Month, 1);
            header.PeriodFrom = start;
            header.PeriodTo = start.AddMonths(1).AddDays(-1);
        }

        return EventResult.Ok();
    }

    // MIQS BeforeSave: runs before ANY save — insert (isNew) or update.
    public override async Task<EventResult> OnBeforeSaveAsync(TimeSheet header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;

        var from = DayOf(header.PeriodFrom);
        var to = DayOf(header.PeriodTo);
        if (from != null && to != null && from > to)
            return EventResult.Cancel("Дата начала периода не может быть позже даты окончания.");

        if (header.Days.Count > 0)
            RollupHours(header);

        return EventResult.Ok();
    }

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(TimeSheet header, bool isNew, EventContext context)
        => next(header, isNew, context);

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(TimeSheet header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(TimeSheet header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(TimeSheet header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(TimeSheet header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before the document is deleted.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // After the document was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // Before posting: validate the whole document; cancel to block posting.
    public override async Task<EventResult> OnBeforePostAsync(TimeSheet header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        // if (header.Number == null)
        //     return EventResult.Cancel("Number is required before posting");
        return EventResult.Ok();
    }

    // After the document was posted (register movements are written).
    public override Task<EventResult> OnAfterPostAsync(TimeSheet header, EventContext context)
        => next(header, context);

    // Before unpost/cancel: about to reverse the document's movements.
    public override Task<EventResult> OnBeforeUnpostAsync(TimeSheet header, EventContext context)
        => next(header, context);

    // After the document's movements were reversed.
    public override Task<EventResult> OnAfterUnpostAsync(TimeSheet header, EventContext context)
        => next(header, context);

    // Human-readable description shown in lists: put it in context.Data["description"].
    public override async Task<EventResult> OnGenerateDescriptionAsync(TimeSheet header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        var from = DayOf(header.PeriodFrom);
        var to = DayOf(header.PeriodTo);
        if (from != null && to != null)
            context.Data["description"] = $"{from:yyyy-MM-dd} – {to:yyyy-MM-dd}";
        return EventResult.Ok();
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
            else sheet.Lines.RemoveAt(i);
        }
        foreach (var pair in byEmp)
        {
            if (seen.Contains(pair.Key)) continue;
            sheet.Lines.Add(new TimeSheetLinesTablePartRow { Employee = pair.Key, Hours = pair.Value });
        }
    }

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(TimeSheet header, string errorMessage, EventContext context)
        => next(header, errorMessage, context);

    // A delete failed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => next(recordId, errorMessage, context);
}
