public partial class TBChainFlatTx
{
    protected override void GetTransactions(TBStockDoc document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Items)
        {
            transactions.Add(new TBStock { Warehouse = document.Warehouse, Item = line.Item ?? Guid.Empty, Quantity = 100m });
        }
    }
}