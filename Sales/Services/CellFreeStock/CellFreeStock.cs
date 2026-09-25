#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

// Сколько товар документ ещё может забрать с ячейки.
// Физический Stock плюс то, что этот документ уже списал (повторное
// проведение не отказывает само себе), минус ReservedStock чужих документов.
// Свой резерв не входит: отбор подтверждает именно свою ячейку хранения.
//
// ReservedStock хранит товар и ячейку аналитиками. IDataService к этой
// таблице не подходит: он сортирует по CreatedDateTime, которой там нет.
public partial class CellFreeStock
{
    private static readonly Guid ItemAnalytic = Guid.Parse("4384343f-aa55-4cc4-9e0d-9893bfc47fb3");
    private static readonly Guid CellAnalytic = Guid.Parse("976f4076-722f-4764-a543-68f35d1189e1");

    private readonly ITotalsManager _totals;
    private readonly ISqlService _sql;

    public CellFreeStock(ITotalsManager totals, ISqlService sql)
    {
        _totals = totals;
        _sql = sql;
    }

    public async Task<decimal> FreeForDocumentAsync(Guid documentId, Guid cell, Guid item)
    {
        if (cell == Guid.Empty || item == Guid.Empty) return 0m;

        var dims = new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item };
        var onHand = await _totals.GetBalanceAsync("Stock", "Qty", dims);
        var reserved = await _totals.GetBalanceAsync("ReservedStock", "Qty", dims);
        var ownStock = await OwnStockAsync(documentId, cell, item);
        var ownReserved = await OwnReservedAsync(documentId, cell, item);
        var others = reserved - ownReserved;
        if (others < 0m) others = 0m;
        return onHand - ownStock - others;
    }

    public string Shortage(decimal need, decimal free)
        => "Недостаточно свободного остатка: требуется "
           + need.ToString("0.####", CultureInfo.InvariantCulture)
           + ", свободно "
           + free.ToString("0.####", CultureInfo.InvariantCulture);

    private async Task<decimal> OwnStockAsync(Guid documentId, Guid cell, Guid item)
    {
        if (documentId == Guid.Empty) return 0m;
        decimal sum = 0m;
        foreach (var row in await _totals.QueryMovementsAsync("Stock", $"[DocumentMetaId] = '{documentId}'"))
        {
            if (!TryGuid(row.GetValueOrDefault("Item"), out var rowItem) || rowItem != item) continue;
            if (TryGuid(row.GetValueOrDefault("Cell"), out var rowCell) && rowCell != cell) continue;
            sum += QtyOf(row);
        }
        return sum;
    }

    private async Task<decimal> OwnReservedAsync(Guid documentId, Guid cell, Guid item)
    {
        if (documentId == Guid.Empty) return 0m;
        var rows = await _sql.SelectAsync(
            "SELECT SUM(m.[Qty]) AS [Qty] " +
            "FROM [TR_ReservedStock] m " +
            "INNER JOIN [MetaAnalyticSetValues] item " +
            "ON item.[SetMetaId] = m.[AnalyticSetMetaId] " +
            $"AND item.[AnalyticMetaId] = '{ItemAnalytic:D}' " +
            "INNER JOIN [MetaAnalyticSetValues] cell " +
            "ON cell.[SetMetaId] = m.[AnalyticSetMetaId] " +
            $"AND cell.[AnalyticMetaId] = '{CellAnalytic:D}' " +
            "WHERE m.[DocumentMetaId] = @document " +
            "AND item.[Value] = @item AND cell.[Value] = @cell",
            new Dictionary<string, object?>
            {
                ["document"] = documentId,
                ["item"] = item.ToString("D"),
                ["cell"] = cell.ToString("D"),
            });
        return rows.Count == 0 ? 0m : QtyOf(rows[0]);
    }

    private static decimal QtyOf(Dictionary<string, object?> row)
    {
        var raw = row.GetValueOrDefault("Qty");
        return raw is null ? 0m : Convert.ToDecimal(raw);
    }

    private static bool TryGuid(object? value, out Guid id)
    {
        switch (value)
        {
            case Guid parsed:
                id = parsed;
                return true;
            case string text when Guid.TryParse(text, out var parsed):
                id = parsed;
                return true;
            default:
                id = Guid.Empty;
                return false;
        }
    }
}
