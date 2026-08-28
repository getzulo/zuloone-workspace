#nullable enable

// Перемещение между ячейками — естественная сбалансированная ПАРА двойной записи
// Stock: outcome −qty у FromLocation, income +qty у ToLocation (одной парой,
// в сумме ноль — товар не создаётся и не уничтожается, только переезжает).
public partial class StockTransferTx
{
    protected override void GetTransactions(StockTransfer document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactionPairs.Add(
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.FromCell).Res("Qty", -line.Quantity),
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.ToCell).Res("Qty", line.Quantity));
        }
    }
}
