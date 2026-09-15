#nullable enable
namespace ZuloOne.Runtime.Generated;

// Handler for table-part SalesReturnLines (row class SalesReturnLinesTablePartRow).
// Override OnValidateField / OnFieldChanged. Parent document:
//   var order = Owner<SalesOrder>(context);
// Chain of Command: highest-layer override runs first; next(...) continues
// to the lower link. Forgetting next() is ZOCOC001. [Replace] swallows the chain.
public partial class SalesReturnLinesEventHandler : TypedTablePartEventHandler<SalesReturnLinesTablePartRow>
{
    // public override async Task<EventResult> OnFieldChangedAsync(SalesReturnLinesTablePartRow row, string fieldName, object? value, EventContext context)
    // {
    //     var prior = await next(row, fieldName, value, context);
    //     if (!prior.Success) return prior;
    //     // var header = Owner<SalesOrder>(context);
    //     return EventResult.Ok();
    // }
}
