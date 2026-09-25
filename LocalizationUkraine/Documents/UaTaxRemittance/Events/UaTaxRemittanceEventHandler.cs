using System;
#nullable enable
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Runtime.Events;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class UaTaxRemittanceEventHandler : TypedDocumentEventHandler<UaTaxRemittance>
{
    public override async Task<EventResult> OnBeforeCreateAsync(UaTaxRemittance header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("UaTaxRemittance");
        if (header.LegalEntity == Guid.Empty)
        {
            var createId = createDefaults.Pick(createSeed, "LegalEntity");
            if (createId != Guid.Empty) header.LegalEntity = createId;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforePostAsync(
        UaTaxRemittance document, EventContext context)
    {
        if (document.Subtype != "Paid")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>()
            .GetDocumentAsync<UaTaxRemittance>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;

        if (document.LegalEntity == Guid.Empty)
            return EventResult.Cancel("Укажіть юрособу.");
        if (document.TaxCode == Guid.Empty)
            return EventResult.Cancel("Укажіть код податку (ПДФО або військовий збір).");
        if (lines.Count == 0)
            return EventResult.Cancel("Додайте рядки перерахування.");
        if (lines.Any(l => l.Amount < 0m))
            return EventResult.Cancel("Сума перерахування не може бути від'ємною.");
        if (lines.All(l => l.Amount == 0m))
            return EventResult.Cancel("Сума перерахування має бути більшою за нуль.");

        return EventResult.Ok();
    }
}
