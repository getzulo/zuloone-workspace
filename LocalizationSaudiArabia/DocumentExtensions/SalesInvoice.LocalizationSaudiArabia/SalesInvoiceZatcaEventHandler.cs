#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

[ExtensionOf("SalesRealization")]
public partial class SalesInvoiceZatcaEventHandler : TypedDocumentEventHandler<SalesRealization>
{
    /// <summary>
    /// The envelope is built AFTER POSTING, not after saving.
    ///
    /// <para>TaxRateApplied is stamped by the Sales handler in
    /// <c>OnBeforePostAsync</c>, which runs after the save. Building the UBL on
    /// OnAfterSave therefore read a rate of zero, and every XML carried
    /// <c>TaxTotal 0.00</c> while the invoice itself showed 15% — silently,
    /// because no test compared the two. A backdated invoice made it worse: the
    /// rate is resolved on the invoice date, so the envelope could not have had
    /// it yet whatever the order.</para>
    ///
    /// <para>EnsureForInvoiceAsync is idempotent on SourceDocumentId, which is
    /// what makes this safe: OnAfterPost can fire twice when a totals driver
    /// writes movements during the document's own posting.</para>
    /// </summary>
    public override async Task<EventResult> OnAfterPostAsync(SalesRealization header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        if (header.Subtype != "Issued" || header.MetaId == Guid.Empty)
            return EventResult.Ok();
        await context.GetService<ISaudiEInvoice>().EnsureForInvoiceAsync(header.MetaId);
        return EventResult.Ok();
    }
}
