#nullable enable
using System;

// Отгрузка по счёту: −количество с ячейки продажи. Stock односторонний, встречной
// ноги нет. Защита от перепродажи — в событии
// SalesInvoiceEventHandler.OnBeforePostAsync: движковой проверки нет, потому что
// регистр допускает отрицательный остаток.
public partial class SalesStockTx
{

    protected override void GetTransactions(SalesInvoice document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("Stock")
                .Dim("Item", line.Item).Dim("Cell", document.Location).Res("Qty", -line.Quantity));
        }
    }
}
