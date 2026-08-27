#nullable enable
namespace ZuloOne.Runtime.Generated;

// Lifecycle handler for Division records. Each division belongs to exactly one legal
// entity and carries a role (DivisionType); both are enforced as required in metadata.
public partial class DivisionEventHandler : TypedDictionaryEventHandler<Division>
{
    public override Task<EventResult> OnBeforeSaveAsync(Division record, bool isNew, EventContext context)
    {
        if (record.LegalEntity == Guid.Empty)
            return Task.FromResult(EventResult.Cancel("Подразделение должно принадлежать юрлицу"));
        return Task.FromResult(EventResult.Ok());
    }
}
