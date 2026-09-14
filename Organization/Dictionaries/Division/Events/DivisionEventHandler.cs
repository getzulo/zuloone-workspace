#nullable enable
namespace ZuloOne.Runtime.Generated;

// Lifecycle handler for Division records. Each division belongs to exactly one legal
// entity and carries a role (DivisionType); both are enforced as required in metadata.
public partial class DivisionEventHandler : TypedDictionaryEventHandler<Division>
{
    public override async Task<EventResult> OnBeforeSaveAsync(Division record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.LegalEntity == Guid.Empty)
            return EventResult.Cancel("Подразделение должно принадлежать юрлицу");
        return EventResult.Ok();
    }
}
