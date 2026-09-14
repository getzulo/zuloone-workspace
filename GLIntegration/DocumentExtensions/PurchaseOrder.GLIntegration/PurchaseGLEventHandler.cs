#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Purchasing extension: receiving a purchase order is posted to the general ledger
// (Dr inventory / Cr payables). Second consumer of GeneralLedgerService —
// same posting mechanics, only the profile accounts and line captions differ.
// A CHAIN link of PurchaseOrder handlers from GLIntegration (see the note in
// SalesGLEventHandler): the class keeps the base handler name, otherwise the
// script competes with it and never runs at all.
public partial class PurchaseGLEventHandler : TypedDocumentEventHandler<PurchaseOrder>
{
    public override async Task<EventResult> OnAfterPostAsync(PurchaseOrder document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Received") return EventResult.Ok();

        var jeId = await PostToLedgerAsync(document, context);
        if (jeId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(PurchaseOrder header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        var order = await context.GetService<IDocumentManager>().GetDocumentAsync<PurchaseOrder>(header.MetaId);
        if (order == null) return null;
        var pricing = context.GetService<IPricingService>();
        var total = order.Lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice));

        // Legal entity — along Cell → Zone → Store → Division → LegalEntity.
        var loc = await context.GetService<IDictionaryManager<StoreCell>>().GetRecordAsync(order.Location);
        if (loc == null) return null;
        var zone = await context.GetService<IDictionaryManager<StoreZone>>().GetRecordAsync(loc.StoreZone);
        if (zone == null) return null;
        var wh = await context.GetService<IDictionaryManager<Store>>().GetRecordAsync(zone.Store);
        if (wh == null) return null;
        var div = await context.GetService<IDictionaryManager<Division>>().GetRecordAsync(wh.Division);
        if (div == null) return null;

        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(div.LegalEntity);
        if (le == null) return null;

        return await gl.PostAsync(
            order.DocumentDate, le.MetaId, le.Currency, total,
            settings.InventoryAccountCode, settings.PayableAccountCode,
            "Purchase order " + header.MetaId,
            "Запасы по приходу", "Кредиторка перед поставщиком");
    }
}
