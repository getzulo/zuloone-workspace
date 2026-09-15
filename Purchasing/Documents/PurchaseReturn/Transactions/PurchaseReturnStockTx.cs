#nullable enable

public partial class PurchaseReturnStockTx
{
    protected override void GetTransactions(PurchaseReturn document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            if (line.Quantity <= 0m) continue;
            transactions.Add(new RegisterMovementSpec("Stock")
                .Dim("Item", line.Item)
                .Dim("Cell", document.Location)
                .Res("Qty", -line.Quantity));
        }
    }
}
