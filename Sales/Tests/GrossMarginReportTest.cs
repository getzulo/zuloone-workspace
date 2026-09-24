using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Testing;

// Источник отчёта GrossMarginReport — тот же JOIN. Бой без выручки
// в выборку не попадает: левая сторона — TR_Revenue.
public class GrossMarginReportTest : IntegrationTestScriptBase
{
    private static readonly Guid RevenueRegister = Guid.Parse("103cdb23-2d49-4c42-9bf3-3c5a8ce46cdc");
    private static readonly Guid InventoryValueRegister = Guid.Parse("7e0b4d85-1a6f-4c9d-8e5b-8f1a4c7d0e60");
    private static readonly DateTime Day = new(2026, 8, 15);

    private static IRegisterMovementService Movements => GetService<IRegisterMovementService>();
    private static ISqlService Sql => GetService<ISqlService>();

    [IntegrationTest("Отчёт даёт выручку, себестоимость и маржу продажи и не берёт бой")]
    public async Task ReportShowsSaleMarginAndSkipsScrap()
    {
        var item = Db.NewId();
        var sale = Db.NewId();
        var scrap = Db.NewId();
        await PostRevenueAsync(sale, item, 1000m);
        await PostValueAsync(sale, item, -1m, -400m);
        await PostValueAsync(scrap, item, -1m, -50m);

        var row = await RowAsync(item);
        Assert.IsTrue(Dec(row, "Revenue") == 1000m, "выручка 1000, факт {0}", Dec(row, "Revenue"));
        Assert.IsTrue(Dec(row, "Cogs") == 400m, "себестоимость 400, бой 50 вне отчёта, факт {0}", Dec(row, "Cogs"));
        Assert.IsTrue(Dec(row, "Margin") == 600m, "маржа 600, факт {0}", Dec(row, "Margin"));
    }

    [IntegrationTest("Чужой товар на том же документе не смешивается")]
    public async Task OtherItemStaysOnItsOwnRow()
    {
        var item = Db.NewId();
        var other = Db.NewId();
        var sale = Db.NewId();
        await PostRevenueAsync(sale, item, 100m);
        await PostValueAsync(sale, item, -1m, -40m);
        await PostRevenueAsync(sale, other, 999m);
        await PostValueAsync(sale, other, -1m, -1m);

        var row = await RowAsync(item);
        Assert.IsTrue(Dec(row, "Margin") == 60m, "100 − 40, факт {0}", Dec(row, "Margin"));
        var foreign = await RowAsync(other);
        Assert.IsTrue(Dec(foreign, "Revenue") == 999m, "чужой товар своей строкой, факт {0}", Dec(foreign, "Revenue"));
    }

    private async Task<Dictionary<string, object?>> RowAsync(Guid item)
    {
        var rows = await Sql.SelectAsync(@"
SELECT [Revenue] AS [Revenue], [Cogs] AS [Cogs], [Margin] AS [Margin]
FROM (
SELECT
    rev.[MovementDate] AS [MovementDate],
    rev.[DocumentMetaId] AS [DocumentMetaId],
    rev.[Item] AS [Item],
    rev.[Revenue] AS [Revenue],
    COALESCE(cogs.[Cogs], 0) AS [Cogs],
    rev.[Revenue] - COALESCE(cogs.[Cogs], 0) AS [Margin]
FROM (
    SELECT
        MIN(r.[MovementDate]) AS [MovementDate],
        r.[DocumentMetaId] AS [DocumentMetaId],
        item.[Value] AS [Item],
        SUM(r.[Amount]) AS [Revenue]
    FROM [TR_Revenue] r
    INNER JOIN [MetaAnalyticSetValues] item
        ON item.[SetMetaId] = r.[AnalyticSetMetaId]
       AND item.[AnalyticMetaId] = '4384343f-aa55-4cc4-9e0d-9893bfc47fb3'
    GROUP BY r.[DocumentMetaId], item.[Value]
) rev
LEFT JOIN (
    SELECT
        v.[DocumentMetaId] AS [DocumentMetaId],
        item.[Value] AS [Item],
        SUM(-v.[Value]) AS [Cogs]
    FROM [TR_InventoryValue] v
    INNER JOIN [MetaAnalyticSetValues] item
        ON item.[SetMetaId] = v.[AnalyticSetMetaId]
       AND item.[AnalyticMetaId] = '4384343f-aa55-4cc4-9e0d-9893bfc47fb3'
    WHERE v.[Value] < 0
    GROUP BY v.[DocumentMetaId], item.[Value]
) cogs
    ON cogs.[DocumentMetaId] = rev.[DocumentMetaId]
   AND cogs.[Item] = rev.[Item]
) src
WHERE [Item] = @item",
            new Dictionary<string, object?> { ["item"] = item.ToString("D").ToLowerInvariant() });
        return rows.Single();
    }

    private static decimal Dec(IDictionary<string, object?> row, string column)
        => Convert.ToDecimal(row[column], CultureInfo.InvariantCulture);

    private async Task PostRevenueAsync(Guid document, Guid item, decimal amount)
        => await Movements.PostMovementAsync(
            RevenueRegister, document, Day,
            new Dictionary<string, object?>(),
            new Dictionary<string, decimal> { ["Amount"] = amount },
            analytics: new Dictionary<string, object?>
            {
                ["Item"] = item,
                ["Customer"] = Db.NewId(),
                ["SalesContract"] = Db.NewId()
            });

    private async Task PostValueAsync(Guid document, Guid item, decimal qty, decimal value)
        => await Movements.PostMovementAsync(
            InventoryValueRegister, document, Day,
            new Dictionary<string, object?>(),
            new Dictionary<string, decimal> { ["Qty"] = qty, ["Value"] = value },
            analytics: new Dictionary<string, object?> { ["Item"] = item });
}
