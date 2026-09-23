#nullable enable
using ZuloOne.Runtime.Events;

namespace ZuloOne.Runtime.Generated;

public partial class UaFopEsvAccrualEventHandler : TypedDocumentEventHandler<UaFopEsvAccrual>
{
    public override Task<EventResult> OnBeforePostAsync(
        UaFopEsvAccrual document, EventContext context)
    {
        if (document.Subtype != "Posted")
            return Task.FromResult(EventResult.Ok());
        if (document.LegalEntity == Guid.Empty)
            return Task.FromResult(EventResult.Cancel("Укажіть юрособу."));
        if (document.Amount <= 0m)
            return Task.FromResult(EventResult.Cancel("Сума ЄСВ за себе має бути більшою за нуль."));
        return Task.FromResult(EventResult.Ok());
    }
}
