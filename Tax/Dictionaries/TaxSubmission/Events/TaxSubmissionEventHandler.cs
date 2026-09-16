#nullable enable
namespace ZuloOne.Runtime.Generated;

public partial class TaxSubmissionEventHandler : TypedDictionaryEventHandler<TaxSubmission>
{
    public override Task<EventResult> OnBeforeCreateAsync(TaxSubmission record, EventContext context)
        => next(record, context);

    public override Task<EventResult> OnBeforeSaveAsync(TaxSubmission record, bool isNew, EventContext context)
        => next(record, isNew, context);
}
