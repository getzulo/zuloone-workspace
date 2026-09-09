#nullable enable
namespace ZuloOne.Runtime.Generated;

public partial class PaymentTermEventHandler : TypedDictionaryEventHandler<PaymentTerm>
{
    public override Task<EventResult> OnBeforeSaveAsync(PaymentTerm record, bool isNew, EventContext context)
    {
        if (string.IsNullOrWhiteSpace(record.Name))
            return Task.FromResult(EventResult.Cancel("Payment term name is required"));
        return Task.FromResult(EventResult.Ok());
    }
}
