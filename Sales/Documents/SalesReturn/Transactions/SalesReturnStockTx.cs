#nullable enable

public partial class SalesReturnStockTx
{
    protected override void GetTransactions(SalesReturn document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            if (line.Quantity <= 0m) continue;
            transactions.Add(new RegisterMovementSpec("Stock")
                .Dim("Item", line.Item)
                .Dim("Cell", document.Location)
                .Res("Qty", line.Quantity));
        }
    }
}
