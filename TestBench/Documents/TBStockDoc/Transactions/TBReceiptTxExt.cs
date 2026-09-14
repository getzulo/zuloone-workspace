// Code extension (docs/WORKSPACE.md §8.7): subclass of the base script.
// base.GetTransactions(...) — super(); applies only to marker lines.
public class TBReceiptTx_TestBenchExt : TBReceiptTx
{
    protected override void GetTransactions(TBStockDoc document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        base.GetTransactions(document, transactionPairs, transactions);
        foreach (var line in document.Items)
        {
            if (line.Amount == 777.77m)
                transactions.Add(new TBStock { Warehouse = document.Warehouse, Item = line.Item, Quantity = line.Quantity ?? 0m });
        }
    }
}