#nullable enable
using ZuloOne.Services.Contracts;

// Товар уехал в ячейку отбора. Черновой резерв хранения этот переход уже снял.
// Заказу резерв нужен на новой ячейке, пока счёт не отгружен. Ручной отбор без
// заказа ничего не держит: его черновик снялся вместе с переходом.
public partial class PickTaskFollowReserveTx
{
    protected override void GetTransactions(PickTask document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var serves = GetService<ISalesFulfillmentService>()
            .PickServesSalesOrderAsync(document.MetaId).GetAwaiter().GetResult();
        if (!serves) return;

        foreach (var line in document.Lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            if (qty <= 0m || line.Item == Guid.Empty) continue;
            var cell = line.ToCell != Guid.Empty ? line.ToCell : document.FromCell;
            if (cell == Guid.Empty) continue;
            transactions.Add(new RegisterMovementSpec("ReservedStock")
                .An("Item", line.Item)
                .An("Cell", cell)
                .Res("Qty", qty));
        }
    }
}
