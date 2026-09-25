#nullable enable
using ZuloOne.Services.Contracts;

// Отмена счёта снимает проводки самого счёта. Резерв, который держит отбор,
// на счёте не висел — его снимает эта проводка, своей долей строки.
public partial class SalesInvoiceReleasePickHoldTx
{
    protected override void GetTransactions(SalesRealization document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var holds = GetService<ISalesFulfillmentService>()
            .PickHoldsAsync(document.SourceOrder).GetAwaiter().GetResult();

        foreach (var line in document.Lines)
        {
            var need = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            if (need <= 0m) continue;
            for (var i = 0; i < holds.Count && need > 0m; i++)
            {
                if (holds[i]["Item"] is not Guid item || item != line.Item) continue;
                if (holds[i]["Cell"] is not Guid cell || cell == Guid.Empty) continue;
                var left = holds[i]["Qty"] is decimal held ? held : 0m;
                if (left <= 0m) continue;
                var take = left < need ? left : need;
                holds[i]["Qty"] = left - take;
                need -= take;
                transactions.Add(new RegisterMovementSpec("ReservedStock")
                    .An("Item", line.Item)
                    .An("Cell", cell)
                    .Res("Qty", -take));
            }
        }
    }
}
