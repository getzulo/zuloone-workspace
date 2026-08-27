#nullable enable
namespace ZuloOne.Runtime.Generated;

// Lifecycle handler for LegalEntity. Country fixes the tax jurisdiction and functional
// currency fixes the ledger currency — both are the tax/reporting basis, hence mandatory.
// NOTE: auto-install of a country tax pack (ITaxPackInstaller) is added later as an
// extension link once the Tax model exists — not referenced here to keep this compilable.
public partial class LegalEntityEventHandler : TypedDictionaryEventHandler<LegalEntity>
{
    public override Task<EventResult> OnBeforeSaveAsync(LegalEntity record, bool isNew, EventContext context)
    {
        if (record.Country == Guid.Empty || record.Currency == Guid.Empty)
            return Task.FromResult(EventResult.Cancel("Страна и валюта обязательны"));
        return Task.FromResult(EventResult.Ok());
    }
}
