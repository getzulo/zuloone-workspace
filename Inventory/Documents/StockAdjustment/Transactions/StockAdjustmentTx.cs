#nullable enable
using System;

// Корректировка остатков — сбалансированная ПАРА двойной записи Stock со «внешним
// миром» (External): излишек (Quantity > 0) приходит на ячейку из External,
// недостача (Quantity < 0) уходит с ячейки во External. Знак строки задаёт
// направление; сторона outcome всегда ≤ 0 (правило пары). Защита от списания в
// минус — в событии StockAdjustmentEventHandler.OnBeforePostAsync.
public partial class StockAdjustmentTx
{
    private static readonly Guid External = Guid.Parse("e0000000-0000-4000-8000-0000000000e1");

    protected override void GetTransactions(StockAdjustment document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            var q = line.Quantity;
            if (q >= 0m)
                transactionPairs.Add(
                    new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", External).Res("Qty", -q),
                    new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.Cell).Res("Qty", q));
            else
                transactionPairs.Add(
                    new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.Cell).Res("Qty", q),
                    new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", External).Res("Qty", -q));
        }
    }
}
