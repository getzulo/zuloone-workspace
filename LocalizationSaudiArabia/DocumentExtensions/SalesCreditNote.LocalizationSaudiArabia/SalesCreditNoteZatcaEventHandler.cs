#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

[ExtensionOf("SalesCreditNote")]
public partial class SalesCreditNoteZatcaEventHandler : TypedDocumentEventHandler<SalesCreditNote>
{
    /// <summary>
    /// Same hook as the invoice: the envelope is built AFTER POSTING.
    /// OnAfterSave ran before PostSalesCreditNote finished, so a cancelled
    /// post still left a TaxDocument, and the QR had nowhere to go.
    /// </summary>
    public override async Task<EventResult> OnAfterPostAsync(SalesCreditNote header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        if (header.Subtype != SalesCreditNote.Subtypes.Posted || header.MetaId == Guid.Empty)
            return EventResult.Ok();
        await context.GetService<ISaudiEInvoice>().EnsureForCreditNoteAsync(header.MetaId);
        return EventResult.Ok();
    }
}
