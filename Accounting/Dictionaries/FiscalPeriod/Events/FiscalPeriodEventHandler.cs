#nullable enable
namespace ZuloOne.Runtime.Generated;

// Fiscal period validation: the posting window must be a valid interval.
public partial class FiscalPeriodEventHandler : TypedDictionaryEventHandler<FiscalPeriod>
{
    public override Task<EventResult> OnBeforeSaveAsync(FiscalPeriod record, bool isNew, EventContext context)
    {
        if (record.FromDate > record.ToDate)
            return Task.FromResult(EventResult.Cancel("Начало периода должно быть не позже конца"));
        return Task.FromResult(EventResult.Ok());
    }
}
