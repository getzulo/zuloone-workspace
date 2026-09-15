#nullable enable
namespace ZuloOne.Runtime.Generated;

// Handler for table-part TaxCalculationLines (row class TaxCalculationLinesTablePartRow).
// Override OnValidateField / OnFieldChanged. Parent document:
//   var order = Owner<SalesOrder>(context);
// Chain of Command: highest-layer override runs first; next(...) continues
// to the lower link. Forgetting next() is ZOCOC001. [Replace] swallows the chain.
public partial class TaxCalculationLinesEventHandler : TypedTablePartEventHandler<TaxCalculationLinesTablePartRow>
{
    // public override async Task<EventResult> OnFieldChangedAsync(TaxCalculationLinesTablePartRow row, string fieldName, object? value, EventContext context)
    // {
    //     var prior = await next(row, fieldName, value, context);
    //     if (!prior.Success) return prior;
    //     // var header = Owner<SalesOrder>(context);
    //     return EventResult.Ok();
    // }
}
