#nullable enable

// Резерв под реализацию, пока счёт в пути (Reserved → Picking → Packing → Shipped).
// Переход в Issued снимает этот резерв (скрипт к Issued не привязан).
//
// Черновик отбора уже держит ячейку хранения, подтверждённый — ячейку отбора.
// Здесь резервируется только хвост строки, которого отбор не закрыл. Иначе
// одно и то же количество сидело бы дважды, и свободный остаток уходил в минус.
public partial class SalesInvoiceReserveTx
{
    protected override void GetTransactions(SalesRealization document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var holds = GetService<ISalesFulfillmentService>()
            .PickHoldsAsync(document.SourceOrder).GetAwaiter().GetResult();

        foreach (var line in document.Lines)
        {
            var uncovered = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            if (uncovered <= 0m) continue;

            for (var i = 0; i < holds.Count && uncovered > 0m; i++)
            {
                if (holds[i]["Item"] is not Guid item || item != line.Item) continue;
                var left = holds[i]["Qty"] is decimal qty ? qty : 0m;
                if (left <= 0m) continue;
                var take = left < uncovered ? left : uncovered;
                holds[i]["Qty"] = left - take;
                uncovered -= take;
            }

            if (uncovered <= 0m) continue;
            transactions.Add(new RegisterMovementSpec("ReservedStock")
                .An("Item", line.Item)
                .An("Cell", document.Location)
                .Res("Qty", uncovered));
        }
    }
}
