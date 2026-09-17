#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

[ExtensionOf("SalesCreditNote")]
public partial class SalesCreditNoteZatcaEventHandler : TypedDocumentEventHandler<SalesCreditNote>
{
    public override async Task<EventResult> OnAfterSaveAsync(SalesCreditNote header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;
        if (header.Subtype != "Posted" || header.MetaId == Guid.Empty)
            return EventResult.Ok();
        await context.GetService<ISaudiEInvoice>().EnsureForCreditNoteAsync(header.MetaId);
        return EventResult.Ok();
    }
}
