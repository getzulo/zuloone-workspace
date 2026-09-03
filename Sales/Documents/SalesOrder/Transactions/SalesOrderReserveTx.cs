#nullable enable

// Резерв под подтверждённый заказ. Склад и долг не трогаются: товар ещё на
// складе, продажа случится утром со счёта. Откат в Draft / Cancelled / Delivered
// снимает эти движения сам.
public partial class SalesOrderReserveTx
{
    protected override void GetTransactions(SalesOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
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
