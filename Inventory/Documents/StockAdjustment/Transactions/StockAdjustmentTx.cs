#nullable enable
using System;

// Корректировка остатков — ОДИНОЧНАЯ проводка Stock (одинарная запись, как склад в
// MIQS): излишек (Quantity > 0) добавляет qty на ячейку, недостача (Quantity < 0)
// списывает её. Остаток регистра = фактический on-hand, без контрагента-«External».
// Защита от списания в минус — в событии StockAdjustmentEventHandler.OnBeforePostAsync.
public partial class StockAdjustmentTx
{
    protected override void GetTransactions(StockAdjustment document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
            transactions.Add(
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.Cell).Res("Qty", line.Quantity));
    }
}
