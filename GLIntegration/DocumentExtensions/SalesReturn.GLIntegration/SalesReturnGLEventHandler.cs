#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Зеркало SalesGL: продажа Dr дебиторка / Cr выручка, возврат — наоборот.
// Юрлицо с ячейки возврата (поля на документе нет). Не настроена книга —
// возврат проводится, проводки нет.
public partial class SalesReturnGLEventHandler : TypedDocumentEventHandler<SalesReturn>
{
    public override async Task<EventResult> OnAfterPostAsync(SalesReturn document, EventContext context)
    {
        if (document.Subtype != "Posted") return EventResult.Ok();

        var jeId = await PostToLedgerAsync(document, context);
        if (jeId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(SalesReturn header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        var ret = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesReturn>(header.MetaId);
        if (ret == null) return null;

        var pricing = context.GetService<IPricingService>();
        var total = ret.Lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice));
        if (total <= 0m) return null;

        var leId = await context.GetService<IStoreCellService>().GetLegalEntityAsync(ret.Location);
        if (leId is not Guid id || id == Guid.Empty) return null;
        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(id);
        if (le == null) return null;

        return await gl.PostAsync(
            ret.DocumentDate, le.MetaId, le.Currency, total,
            settings.RevenueAccountCode, settings.ArAccountCode,
            "Sales return " + header.MetaId,
            "Сторно выручки", "Сторно дебиторки");
    }
}
