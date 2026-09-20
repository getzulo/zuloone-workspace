#nullable enable
namespace ZuloOne.Runtime.Generated;

// Lifecycle handler for LegalEntity. Country fixes the tax jurisdiction and functional
// currency fixes the ledger currency — both are the tax/reporting basis, hence mandatory.
// Country tax packs: ITaxPackInstaller lives in Tax. This model cannot
// reference it (Tax → Organization). Inventory (layer 2) calls it from a
// CoC link on this dictionary and from ApplyOrg.
public partial class LegalEntityEventHandler : TypedDictionaryEventHandler<LegalEntity>
{
    public override async Task<EventResult> OnBeforeSaveAsync(LegalEntity record, bool isNew, EventContext context){
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.Country == Guid.Empty || record.Currency == Guid.Empty)
            return EventResult.Cancel("Страна и валюта обязательны");
        return EventResult.Ok();
    }
}
