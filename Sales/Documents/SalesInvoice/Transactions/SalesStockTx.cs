#nullable enable
using System;

// Отгрузка по счёту — сбалансированная ПАРА двойной записи Stock: товар уходит
// с ячейки продажи во «внешний мир» (External). Защита от перепродажи — в
// событии SalesInvoiceEventHandler.OnBeforePostAsync (движковой проверки нет,
// т.к. allowNegativeBalance=true для ledger-модели).
public partial class SalesStockTx
{
    private static readonly Guid External = Guid.Parse("e0000000-0000-4000-8000-0000000000e1");

    protected override void GetTransactions(SalesInvoice document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactionPairs.Add(
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Location", document.Location).Res("Qty", -line.Quantity),
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Location", External).Res("Qty", line.Quantity));
        }
    }
}
