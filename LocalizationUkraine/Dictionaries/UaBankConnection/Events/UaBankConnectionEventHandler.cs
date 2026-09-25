using System;
using ZuloOne.Services.Contracts;
#nullable enable
namespace ZuloOne.Runtime.Generated;

// Owner of UaBankConnection events: link 0, does not call next().
public partial class UaBankConnectionEventHandler : TypedDictionaryEventHandler<UaBankConnection>
{
    public override async Task<EventResult> OnBeforeCreateAsync(UaBankConnection record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("UaBankConnection");
        if (record.LegalEntity == Guid.Empty)
        {
            var createId = createDefaults.Pick(createSeed, "LegalEntity");
            if (createId != Guid.Empty) record.LegalEntity = createId;
        }
        return EventResult.Ok();
    }

}
