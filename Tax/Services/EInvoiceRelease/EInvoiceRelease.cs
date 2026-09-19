#nullable enable
using System;
using System.Threading.Tasks;

// Country-blind door for "may this document be given to the buyer".
// Tax itself never blocks: e-invoice policy is a country pack's question.
// LocalizationSaudiArabia wraps and delegates to ISaudiEInvoice.
public partial class EInvoiceRelease
{
    /// <summary>
    /// Null — the source document may be printed or handed over.
    /// Otherwise a reason the buyer must not receive it yet.
    /// </summary>
    public Task<string?> BuyerReleaseBlockAsync(Guid sourceId)
        => Task.FromResult<string?>(null);
}
