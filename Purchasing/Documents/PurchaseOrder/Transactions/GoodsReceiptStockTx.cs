#nullable enable
using System;

// Оприходование заказа поставщику — сбалансированная ПАРА двойной записи Stock:
// товар приходит из «внешнего мира» (External) на принимающую ячейку.
public partial class GoodsReceiptStockTx
{
    private static readonly Guid External = Guid.Parse("e0000000-0000-4000-8000-0000000000e1");

    protected override void GetTransactions(PurchaseOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactionPairs.Add(
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", External).Res("Qty", -line.Quantity),
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.Location).Res("Qty", line.Quantity));
        }
    }
}
