#nullable enable
using ZuloOne.Runtime.Events;

namespace ZuloOne.Runtime.Generated;

public partial class UaVatRefundEventHandler : TypedDocumentEventHandler<UaVatRefund>
{
    public override Task<EventResult> OnBeforePostAsync(
        UaVatRefund document, EventContext context)
    {
        if (document.Subtype != "Posted")
            return Task.FromResult(EventResult.Ok());
        if (document.LegalEntity == Guid.Empty)
            return Task.FromResult(EventResult.Cancel("Укажіть юрособу."));
        if (document.Amount <= 0m)
            return Task.FromResult(EventResult.Cancel("Сума заяви на відшкодування має бути більшою за нуль."));
        return Task.FromResult(EventResult.Ok());
    }
}
