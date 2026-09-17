#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

[ExtensionOf("SalesRealization")]
public partial class SalesInvoiceZatcaEventHandler : TypedDocumentEventHandler<SalesRealization>
{
    public override async Task<EventResult> OnAfterSaveAsync(SalesRealization header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;
        if (header.Subtype != "Issued" || header.MetaId == Guid.Empty)
            return EventResult.Ok();
        await context.GetService<ISaudiEInvoice>().EnsureForInvoiceAsync(header.MetaId);
        return EventResult.Ok();
    }
}
