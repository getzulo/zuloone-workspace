#nullable enable

// Приёмка → хранение: на строку пара двойной записи Stock — минус из ячейки
// приёмки (FromCell, шапка), плюс в ячейку хранения (ToCell, строка).
public partial class PutAwayTaskTx
{
    protected override void GetTransactions(PutAwayTask document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactionPairs.Add(
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.FromCell).Res("Qty", -line.Quantity),
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", line.ToCell).Res("Qty", line.Quantity));
        }
    }
}
