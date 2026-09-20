#nullable enable
using System;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Inventory is layer 2 — it may extend LegalEntity. Tax cannot (same Base
// layer as Organization) and Organization cannot reference Tax (cycle).
// Pack failure must not refuse the save: a missing localization model is
// not a reason to block a legal entity.
[ExtensionOf("LegalEntity")]
public partial class LegalEntityTaxPackEventHandler : TypedDictionaryEventHandler<LegalEntity>
{
    public override async Task<EventResult> OnAfterInsertAsync(LegalEntity record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        await InstallAsync(record, context);
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnAfterUpdateAsync(LegalEntity record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        await InstallAsync(record, context);
        return EventResult.Ok();
    }

    private static async Task InstallAsync(LegalEntity record, EventContext context)
    {
        if (record.Country == Guid.Empty) return;
        var country = await context.GetService<IDictionaryManager<Country>>()
            .GetRecordAsync(record.Country);
        var iso = country?.CodeISO2;
        if (string.IsNullOrWhiteSpace(iso)) return;
        await context.GetService<ITaxPackInstaller>().InstallForCountryAsync(iso);
    }
}
