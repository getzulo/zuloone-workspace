#nullable enable

// Резерв под реализацию. Используется пока счёт в пути (Reserved → Picking → Packing → Shipped).
// Переход в Issued СНИМАЕТ этот резерв (скрипт не привязан к Issued)
// и ЗАПИСЫВАЕТ списание склада скриптами SalesStockTx / SalesRevenueTx / SalesReceivableTx.
// Класс назван отдельно от SalesOrderReserveTx, т.к. имена классов скриптов уникальны
// во всём воркспейсе.
public partial class SalesInvoiceReserveTx
{
    protected override void GetTransactions(SalesInvoice document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            var qty = line.Quantity;
            if (qty <= 0m) continue;
            transactions.Add(new RegisterMovementSpec("ReservedStock")
                .An("Item", line.Item)
                .An("Cell", document.Location)
                .Res("Qty", qty));
        }
    }
}
