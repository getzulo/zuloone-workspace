public partial class TBIssueTx
{
    protected override void GetTransactions(TBStockDoc document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Items)
        {
            transactions.Add(new TBStock { Warehouse = document.Warehouse, Item = line.Item ?? Guid.Empty, Quantity = -(line.Quantity ?? 0m) });
            transactions.Add(new TBFifo { Item = line.Item ?? Guid.Empty, Quantity = -(line.Quantity ?? 0m), Amount = 0m });
        }
    }
}