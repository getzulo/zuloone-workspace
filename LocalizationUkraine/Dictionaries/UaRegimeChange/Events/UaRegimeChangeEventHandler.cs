using System;
using ZuloOne.Services.Contracts;
#nullable enable
namespace ZuloOne.Runtime.Generated;

// Handler for UaRegimeChange. Override only the hooks you need.
// This is the OWNER: it is link 0 of the chain, so it does not call next().
// A handler in ANOTHER model declares itself with [ExtensionOf("UaRegimeChange")] and
// must call next(...) from every override (ZOCOC001) — work AFTER next() sees
// what the owner wrote. [Replace] swallows the chain on purpose.
public partial class UaRegimeChangeEventHandler : TypedDictionaryEventHandler<UaRegimeChange>
{
    public override async Task<EventResult> OnBeforeCreateAsync(UaRegimeChange record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("UaRegimeChange");
        if (record.EffectiveFrom.Year < 1902)
        {
            var createDay = createDefaults.PickDay(createSeed, "EffectiveFrom");
            if (createDay.Year >= 1902) record.EffectiveFrom = createDay;
        }
        if (record.LegalEntity == Guid.Empty)
        {
            var createId = createDefaults.Pick(createSeed, "LegalEntity");
            if (createId != Guid.Empty) record.LegalEntity = createId;
        }
        return EventResult.Ok();
    }

    // public override async Task<EventResult> OnBeforeSaveAsync(UaRegimeChange record, bool isNew, EventContext context)
    // {
    //     return EventResult.Ok();
    // }

    // Live from the card (no Save): OnValidateField then OnFieldChanged.
    // public override async Task<EventResult> OnFieldChangedAsync(UaRegimeChange record, string fieldName, object? value, EventContext context)
    // {
    //     return EventResult.Ok();
    // }
}
