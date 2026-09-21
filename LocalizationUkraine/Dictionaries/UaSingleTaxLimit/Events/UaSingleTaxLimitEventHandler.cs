#nullable enable
namespace ZuloOne.Runtime.Generated;

// Handler for UaSingleTaxLimit. Override only the hooks you need.
// This is the OWNER: it is link 0 of the chain, so it does not call next().
// A handler in ANOTHER model declares itself with [ExtensionOf("UaSingleTaxLimit")] and
// must call next(...) from every override (ZOCOC001) — work AFTER next() sees
// what the owner wrote. [Replace] swallows the chain on purpose.
public partial class UaSingleTaxLimitEventHandler : TypedDictionaryEventHandler<UaSingleTaxLimit>
{
    // public override async Task<EventResult> OnBeforeSaveAsync(UaSingleTaxLimit record, bool isNew, EventContext context)
    // {
    //     return EventResult.Ok();
    // }

    // Live from the card (no Save): OnValidateField then OnFieldChanged.
    // public override async Task<EventResult> OnFieldChangedAsync(UaSingleTaxLimit record, string fieldName, object? value, EventContext context)
    // {
    //     return EventResult.Ok();
    // }
}
