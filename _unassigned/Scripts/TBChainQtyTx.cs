public partial class TBChainQtyTx
{
    protected override void GetTransactions(TBStockDoc document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Items)
        {
            transactions.Add(new TBStock { Warehouse = document.Warehouse, Item = line.Item ?? Guid.Empty, Quantity = line.Quantity ?? 0m });
        }
    }
}