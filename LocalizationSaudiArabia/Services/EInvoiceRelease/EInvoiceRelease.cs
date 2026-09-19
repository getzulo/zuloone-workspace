#nullable enable
using System;
using System.Threading.Tasks;
using ZuloOne.Runtime;
using ZuloOne.Services.Contracts;

namespace LocalizationSaudiArabia;

// Same contract name as Tax.EInvoiceRelease — CoC wrap, not a second door.
public partial class EInvoiceRelease
{
    public async Task<string?> BuyerReleaseBlockAsync(Guid sourceId)
    {
        var prior = await nextAsync<string?>(sourceId);
        if (!string.IsNullOrEmpty(prior)) return prior;
        return await ScriptServices.Get<ISaudiEInvoice>().BuyerReleaseBlockAsync(sourceId);
    }
}
