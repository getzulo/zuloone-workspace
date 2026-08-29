#nullable enable
using System;

// Корректировка остатков — ОДИНОЧНАЯ проводка Stock (одинарная запись, как склад в
// MIQS): излишек (Quantity > 0) добавляет qty на ячейку, недостача (Quantity < 0)
// списывает её. Остаток регистра = фактический on-hand, без контрагента-«External».
// Защита от списания в минус — в событии StockAdjustmentEventHandler.OnBeforePostAsync.
//
// В регистр уходит BaseQuantity (базовая единица товара, считает платформа при
// сохранении строки); ноль = «единица не указана, пересчёта не было» → введённое
// количество и есть базовое. Знак при пересчёте сохраняется, так что недостача
// остаётся недостачей.
public partial class StockAdjustmentTx
{
    protected override void GetTransactions(StockAdjustment document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            transactions.Add(
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.Cell).Res("Qty", qty));
        }
    }
}
