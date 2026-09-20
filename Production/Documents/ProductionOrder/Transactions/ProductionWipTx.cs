#nullable enable
using System;

// Незавершёнка — количество изделия, пока заказ в Released. Finished этот
// скрипт не пишет: Mix отката Released сам снимает WIP. Прыжок Draft→Finished
// WIP не трогает — незавершёнки не было.
public partial class ProductionWipTx
{
    protected override void GetTransactions(ProductionOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var qty = document.BaseQuantity != 0m ? document.BaseQuantity : document.Quantity;
        if (qty <= 0m) return;
        if (document.Product == Guid.Empty || document.Location == Guid.Empty) return;

        transactions.Add(new RegisterMovementSpec("WorkInProgress")
            .An("Item", document.Product)
            .An("Cell", document.Location)
            .Res("Qty", qty));
    }
}
