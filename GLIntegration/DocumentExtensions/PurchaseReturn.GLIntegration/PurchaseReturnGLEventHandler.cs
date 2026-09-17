#nullable enable
using System;
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;
[ExtensionOf("PurchaseReturn")]

public partial class PurchaseReturnGLEventHandler : TypedDocumentEventHandler<PurchaseReturn>
{
    public override async Task<EventResult> OnAfterPostAsync(PurchaseReturn document, EventContext context)
    {
        var prior = await next(document, context);
        if (!prior.Success) return prior;
        if (document.Subtype != "Posted") return EventResult.Ok();

        var jeId = await PostToLedgerAsync(document, context);
        if (jeId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);
        return EventResult.Ok();
    }

    private static async Task<Guid?> PostToLedgerAsync(PurchaseReturn header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        var ret = await context.GetService<IDocumentManager>().GetDocumentAsync<PurchaseReturn>(header.MetaId);
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
            settings.PayableAccountCode, settings.InventoryAccountCode,
            "Purchase return " + header.MetaId,
            "Сторно кредиторки", "Сторно запасов");
    }
}
