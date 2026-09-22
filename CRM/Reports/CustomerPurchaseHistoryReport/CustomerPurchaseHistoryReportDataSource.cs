#nullable enable
using System.Collections.Generic;
using ZuloOne.Runtime.Reports;

// Источник «чёта «история покупок». Платформа агрегирует In/Add/Sub/Out
// по этому SQL — скрипт не считает итоги сам.
//
// ПОЧЕМУ НЕ TR_Revenue. Выручка режет клиента и товар аналитиками AnalyticSet,
// не колонками движения. CustomReport требует колонки в SELECT. Строки
// реализации — обычные поля, и это тот же факт продажи.
//
// Скобки [Имя] нормализует ISqlDialect: на SQL Server остаются скобки,
// на Postgres становятся кавычками.
public partial class CustomReportCustomerPurchaseHistoryReport
{
    public override string GetTransactionsSql() => @"
SELECT
    h.[DocumentDate] AS MovementDate,
    h.[MetaId] AS DocumentMetaId,
    h.[Customer] AS Customer,
    l.[Item] AS Item,
    l.[Quantity] AS Quantity,
    l.[Quantity] * l.[UnitPrice] AS Amount
FROM [SalesRealization] h
INNER JOIN [TP_SalesInvoiceLines] l ON l.[OwnerMetaId] = h.[MetaId]
WHERE h.[Subtype] NOT IN (N'Draft', N'Cancelled')";

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
                Name = "Customer", Caption = "Customer", DatabaseName = "Customer",
                DictionaryName = "Customer", DisplayFormat = "{Name}",
            },
            new SpaceTotalColumn
            {
                Name = "Item", Caption = "Item", DatabaseName = "Item",
                DictionaryName = "Item", DisplayFormat = "{Name}",
            },
            new VariableTotalColumn
            {
                Name = "Quantity", Caption = "Quantity", DatabaseName = "Quantity",
            },
            new VariableTotalColumn
            {
                Name = "Amount", Caption = "Amount", DatabaseName = "Amount",
            },
        };
    }

    public override IEnumerable<string> GetSortOrderColumns()
        => new[] { "Customer", "Item" };
}
