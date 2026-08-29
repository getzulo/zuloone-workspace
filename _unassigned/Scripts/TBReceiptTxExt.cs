// Расширение кода (docs/WORKSPACE.md §8.7): наследник базового скрипта.
// base.GetTransactions(...) — super(); действует только на строки-маркеры.
public class TBReceiptTx_TestBenchExt : TBReceiptTx
{
    protected override void GetTransactions(TBStockDoc document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        base.GetTransactions(document, transactionPairs, transactions);
        foreach (var line in document.Items)
        {
            if (line.Amount == 777.77m)
                transactions.Add(new TBStock { Warehouse = document.Warehouse, Item = line.Item ?? Guid.Empty, Quantity = line.Quantity ?? 0m });
        }
    }
}