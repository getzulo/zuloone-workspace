using System;
#nullable enable
using ZuloOne.Runtime.Events;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class UaFopEsvAccrualEventHandler : TypedDocumentEventHandler<UaFopEsvAccrual>
{
    public override async Task<EventResult> OnBeforeCreateAsync(UaFopEsvAccrual header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("UaFopEsvAccrual");
        if (header.LegalEntity == Guid.Empty)
        {
            var createId = createDefaults.Pick(createSeed, "LegalEntity");
            if (createId != Guid.Empty) header.LegalEntity = createId;
        }
        return EventResult.Ok();
    }

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
