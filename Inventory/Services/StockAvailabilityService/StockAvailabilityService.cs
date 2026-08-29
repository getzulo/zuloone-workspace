using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

// Проверка наличия остатка в ячейке: хватает ли `qty` товара `item` в ячейке
// `cell` по регистру Stock. Переиспружется Production/Sales перед списанием строго
// из ячейки отбора.
public partial class StockAvailabilityService
{
    private readonly ITotalsManager _totals;

    public StockAvailabilityService(ITotalsManager totals)
    {
        _totals = totals;
    }

    /// <summary>Текущий остаток товара в ячейке по регистру Stock (0, если строки нет).</summary>
    public async Task<decimal> OnHandAsync(Guid cell, Guid item)
    {
        // Отсутствующая строка остатка — это ноль, а не ошибка; менеджер трактует
        // её так сам, поэтому проверки на null здесь больше нет.
        return await _totals.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Item"] = item, ["Cell"] = cell });
    }

    /// <summary>Хватает ли остатка в ячейке под требуемое количество.</summary>
    public async Task<bool> HasSufficientStockAsync(Guid cell, Guid item, decimal qty)
        => await OnHandAsync(cell, item) >= qty;
}
