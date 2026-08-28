#nullable enable

// Приёмка → хранение: на строку ДВЕ одиночные проводки Stock (одинарная запись) —
// минус из ячейки приёмки (FromCell, шапка), плюс в ячейку хранения (ToCell, строка).
public partial class PutAwayTaskTx
{
    protected override void GetTransactions(PutAwayTask document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.FromCell).Res("Qty", -line.Quantity));
            transactions.Add(new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", line.ToCell).Res("Qty", line.Quantity));
        }
    }
}
