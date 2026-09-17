#nullable enable

// Перемещение между ячейками — ДВЕ одиночные проводки Stock (одинарная запись):
// outcome −qty у FromCell, income +qty у ToCell. Товар не создаётся и не
// уничтожается — просто переезжает между ячейками.
//
// В регистр уходит BaseQuantity (базовая единица товара, считает платформа при
// сохранении строки); ноль = «единица не указана, пересчёта не было» → введённое
// количество и есть базовое. Обе ноги берут ОДНО значение — иначе перемещение
// создавало бы или уничтожало товар.
public partial class StockTransferTx
{
    protected override void GetTransactions(StockTransfer document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            transactions.Add(new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.FromCell).Res("Qty", -qty));
            transactions.Add(new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.ToCell).Res("Qty", qty));
        }
    }
}
