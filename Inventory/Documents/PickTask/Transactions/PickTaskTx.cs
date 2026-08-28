#nullable enable

// Хранение → отбор: на строку пара двойной записи Stock — минус из ячейки
// хранения (FromCell, шапка), плюс в ячейку отбора (ToCell, строка).
public partial class PickTaskTx
{
    protected override void GetTransactions(PickTask document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactionPairs.Add(
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.FromCell).Res("Qty", -line.Quantity),
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", line.ToCell).Res("Qty", line.Quantity));
        }
    }
}
