using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

// On-hand check in a cell: whether `qty` of `item` is available in
// `cell` on the Stock register. Reused by Production/Sales before a write-off
// strictly from the picking cell.
public partial class StockAvailabilityService
{
    private readonly ITotalsManager _totals;

    public StockAvailabilityService(ITotalsManager totals)
    {
        _totals = totals;
    }

    /// <summary>Current on-hand of the item in the cell on Stock (0 if there is no row).</summary>
    public async Task<decimal> OnHandAsync(Guid cell, Guid item)
    {
        // A missing balance row is zero, not an error; the manager already
        // treats it that way, so there is no null check here anymore.
        return await _totals.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Item"] = item, ["Cell"] = cell });
    }

    /// <summary>Whether the cell has enough on-hand for the required quantity.</summary>
    public async Task<bool> HasSufficientStockAsync(Guid cell, Guid item, decimal qty)
        => await OnHandAsync(cell, item) >= qty;
}
