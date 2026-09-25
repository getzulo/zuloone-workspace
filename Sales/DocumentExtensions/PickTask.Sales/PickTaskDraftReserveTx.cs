#nullable enable

// Черновик отбора ещё не двигает Stock. Резерв садится на ячейку хранения,
// иначе второй заказ видит ту же ячейку свободной и пишет себе такой же черновик.
public partial class PickTaskDraftReserveTx
{
    protected override void GetTransactions(PickTask document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            if (qty <= 0m || line.Item == Guid.Empty || document.FromCell == Guid.Empty) continue;
            transactions.Add(new RegisterMovementSpec("ReservedStock")
                .An("Item", line.Item)
                .An("Cell", document.FromCell)
                .Res("Qty", qty));
        }
    }
}
