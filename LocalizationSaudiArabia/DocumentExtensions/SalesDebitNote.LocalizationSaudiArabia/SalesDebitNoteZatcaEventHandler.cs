#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

[ExtensionOf("SalesDebitNote")]
public partial class SalesDebitNoteZatcaEventHandler : TypedDocumentEventHandler<SalesDebitNote>
{
    /// <summary>
    /// Same hook as the invoice and credit note: the envelope is built
    /// AFTER POSTING, not on save of a draft.
    /// </summary>
    public override async Task<EventResult> OnAfterPostAsync(SalesDebitNote header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        if (header.Subtype != SalesDebitNote.Subtypes.Posted || header.MetaId == Guid.Empty)
            return EventResult.Ok();
        await context.GetService<ISaudiEInvoice>().EnsureForDebitNoteAsync(header.MetaId);
        return EventResult.Ok();
    }
}
