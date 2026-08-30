using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;

// Проверка наличия остатка в ячейке: хватает ли `qty` товара `item` в ячейке
// `cell` по регистру Stock. Переиспружется Production/Sales перед списанием строго
// из ячейки отбора.
public partial class StockAvailabilityService
{
    private static readonly Guid StockRegister = Guid.Parse("83559331-ac7f-46da-87a8-7da599ef6f41");
    private readonly IRegisterMovementService _stock;

    public StockAvailabilityService(IRegisterMovementService stock)
    {
        _stock = stock;
    }

    /// <summary>Текущий остаток товара в ячейке по регистру Stock (0, если строки нет).</summary>
    public async Task<decimal> OnHandAsync(Guid cell, Guid item)
    {
        var bal = await _stock.GetBalanceAsync(StockRegister,
            new Dictionary<string, object?> { ["Item"] = item, ["Cell"] = cell });
        return bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
    }

    /// <summary>Хватает ли остатка в ячейке под требуемое количество.</summary>
    public async Task<bool> HasSufficientStockAsync(Guid cell, Guid item, decimal qty)
        => await OnHandAsync(cell, item) >= qty;
}
