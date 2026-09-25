#nullable enable
namespace ZuloOne.Runtime.Generated;

// Handler for table-part ExamAttemptLines (row class ExamAttemptLinesTablePartRow).
// Override OnValidateField / OnFieldChanged. Parent document:
//   var order = Owner<SalesOrder>(context);
// This is the OWNER: link 0 of the chain, so it does not call next(). A handler
// in ANOTHER model declares [ExtensionOf("ExamAttemptLines")] and must call next(...).
public partial class ExamAttemptLinesEventHandler : TypedTablePartEventHandler<ExamAttemptLinesTablePartRow>
{
    // public override async Task<EventResult> OnFieldChangedAsync(ExamAttemptLinesTablePartRow row, string fieldName, object? value, EventContext context)
    // {
    //     // var header = Owner<SalesOrder>(context);
    //     return EventResult.Ok();
    // }
}
