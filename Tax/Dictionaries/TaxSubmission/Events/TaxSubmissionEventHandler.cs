using System;
using ZuloOne.Services.Contracts;
#nullable enable
namespace ZuloOne.Runtime.Generated;

public partial class TaxSubmissionEventHandler : TypedDictionaryEventHandler<TaxSubmission>
{
    public override async Task<EventResult> OnBeforeCreateAsync(TaxSubmission record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("TaxSubmission");
        if (record.LegalEntity == Guid.Empty)
        {
            var createId = createDefaults.Pick(createSeed, "LegalEntity");
            if (createId != Guid.Empty) record.LegalEntity = createId;
        }
        return EventResult.Ok();
    }

    public override Task<EventResult> OnBeforeSaveAsync(TaxSubmission record, bool isNew, EventContext context)
        => next(record, isNew, context);
}
