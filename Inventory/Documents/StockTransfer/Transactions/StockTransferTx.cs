#nullable enable

// Перемещение между ячейками — ДВЕ одиночные проводки Stock (одинарная запись):
// outcome −qty у FromCell, income +qty у ToCell. Товар не создаётся и не
// уничтожается — просто переезжает между ячейками.
public partial class StockTransferTx
{
    protected override void GetTransactions(StockTransfer document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.FromCell).Res("Qty", -line.Quantity));
            transactions.Add(new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.ToCell).Res("Qty", line.Quantity));
        }
    }
}
