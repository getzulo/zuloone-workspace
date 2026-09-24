#nullable enable
using System.Collections.Generic;
using ZuloOne.Runtime.Reports;

// Маржа продажи: выручка минус списание InventoryValue на том же документе
// и том же товаре. Бой и отпуск в производство выручки не пишут — в отчёт
// не входят.
//
// ПОЧЕМУ JOIN, А НЕ КОЛОНКА. Item у Revenue и InventoryValue — аналитика
// AnalyticSet, не измерение. CustomReport требует колонку в SELECT.
// MetaAnalyticSetValues раскрывает набор так же, как отчёт регистра:
// AnalyticMetaId — аналитика Item (Inventory/Analytics/Item.json).
// Псевдонимы в скобках: Postgres сворачивает неэкранированное имя.
public partial class CustomReportGrossMarginReport
{
    public override string GetTransactionsSql() => @"
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
   AND cogs.[Item] = rev.[Item]";

    public override IEnumerable<TotalColumn> GetReportColumns()
    {
        return new TotalColumn[]
        {
            new DateTotalColumn
            {
                Name = "TransactionDate", Caption = "Date", DatabaseName = "MovementDate",
            },
            new DocumentTotalColumn
            {
                Name = "Document", Caption = "Document", DatabaseName = "DocumentMetaId",
            },
            new SpaceTotalColumn
            {
                Name = "Item", Caption = "Item", DatabaseName = "Item",
                DictionaryName = "Item", DisplayFormat = "{Name}",
            },
            new VariableTotalColumn
            {
                Name = "Revenue", Caption = "Revenue", DatabaseName = "Revenue",
            },
            new VariableTotalColumn
            {
                Name = "Cogs", Caption = "Cost", DatabaseName = "Cogs",
            },
            new VariableTotalColumn
            {
                Name = "Margin", Caption = "Margin", DatabaseName = "Margin",
            },
        };
    }

    public override IEnumerable<string> GetSortOrderColumns()
        => new[] { "Item" };
}
