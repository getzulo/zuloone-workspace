#nullable enable
namespace ZuloOne.Runtime.Generated;

// Handler for Certificate. Override only the hooks you need.
// This is the OWNER: it is link 0 of the chain, so it does not call next().
// A handler in ANOTHER model declares itself with [ExtensionOf("Certificate")] and
// must call next(...) from every override (ZOCOC001) — work AFTER next() sees
// warehouse/GL. [Replace] swallows the chain on purpose.
public partial class CertificateEventHandler : TypedDocumentEventHandler<Certificate>
{
    // public override async Task<EventResult> OnAfterPostAsync(Certificate header, EventContext context)
    // {
    //     return EventResult.Ok();
    // }

    // Live from the card (no Save): OnValidateField then OnFieldChanged.
    // Lines are on header.Lines when the form sent the in-memory graph.
    // public override async Task<EventResult> OnFieldChangedAsync(Certificate header, string fieldName, object? value, EventContext context)
    // {
    //     return EventResult.Ok();
    // }
}
