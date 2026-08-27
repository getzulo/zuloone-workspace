#nullable enable
namespace ZuloOne.Runtime.Generated;

// Lifecycle handler for DivisionType records. Empty template — the classifier needs
// no server-side logic beyond the metadata-declared required/unique constraints.
public partial class DivisionTypeEventHandler : TypedDictionaryEventHandler<DivisionType>
{
    public override Task<EventResult> OnBeforeSaveAsync(DivisionType record, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());
}
