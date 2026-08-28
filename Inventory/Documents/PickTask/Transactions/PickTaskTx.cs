#nullable enable

// Хранение → отбор: на строку ДВЕ одиночные проводки Stock (одинарная запись) —
// минус из ячейки хранения (FromCell, шапка), плюс в ячейку отбора (ToCell, строка).
public partial class PickTaskTx
{
    protected override void GetTransactions(PickTask document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.FromCell).Res("Qty", -line.Quantity));
            transactions.Add(new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", line.ToCell).Res("Qty", line.Quantity));
        }
    }
}
