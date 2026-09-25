using System;
using ZuloOne.Services.Contracts;
#nullable enable
namespace ZuloOne.Runtime.Generated;

public partial class UaLandPlotEventHandler : TypedDictionaryEventHandler<UaLandPlot>
{
    public override async Task<EventResult> OnBeforeCreateAsync(UaLandPlot record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("UaLandPlot");
        if (record.LegalEntity == Guid.Empty)
        {
            var createId = createDefaults.Pick(createSeed, "LegalEntity");
            if (createId != Guid.Empty) record.LegalEntity = createId;
        }
        return EventResult.Ok();
    }

}
