public partial class TBReceiptTx
{
    protected override void GetTransactions(TBStockDoc document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Items)
        {
            transactions.Add(new TBStock { Warehouse = document.Warehouse, Item = line.Item, Quantity = line.Quantity ?? 0m });
            transactions.Add(new TBFifo { Item = line.Item, Quantity = line.Quantity ?? 0m, Amount = line.Amount ?? 0m });
        }
    }
}