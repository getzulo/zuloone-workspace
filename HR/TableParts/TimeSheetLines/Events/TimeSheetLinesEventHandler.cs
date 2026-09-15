#nullable enable
namespace ZuloOne.Runtime.Generated;

// Handler for table-part TimeSheetLines (row class TimeSheetLinesTablePartRow).
// Override OnValidateField / OnFieldChanged. Parent document:
//   var order = Owner<SalesOrder>(context);
// Chain of Command: highest-layer override runs first; next(...) continues
// to the lower link. Forgetting next() is ZOCOC001. [Replace] swallows the chain.
public partial class TimeSheetLinesEventHandler : TypedTablePartEventHandler<TimeSheetLinesTablePartRow>
{
    // public override async Task<EventResult> OnFieldChangedAsync(TimeSheetLinesTablePartRow row, string fieldName, object? value, EventContext context)
    // {
    //     var prior = await next(row, fieldName, value, context);
    //     if (!prior.Success) return prior;
    //     // var header = Owner<SalesOrder>(context);
    //     return EventResult.Ok();
    // }
}
