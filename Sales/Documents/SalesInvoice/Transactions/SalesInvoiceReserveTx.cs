#nullable enable

// Reserve for the realization. Used while the invoice is in transit (Reserved → Picking → Packing → Shipped).
// Transition to Issued LIFTS this reserve (the script is not bound to Issued)
// and WRITES the warehouse write-off via SalesStockTx / SalesRevenueTx / SalesReceivableTx.
// Named separately from SalesOrderReserveTx because script class names are unique
// across the workspace.
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
