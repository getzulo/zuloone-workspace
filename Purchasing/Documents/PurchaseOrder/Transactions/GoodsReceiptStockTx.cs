#nullable enable
using System;

// Оприходование заказа поставщику: +количество на принимающую ячейку. Stock —
// односторонний накопительный регистр, поэтому встречной ноги нет, а остаток
// ячейки и есть фактическое наличие.
public partial class GoodsReceiptStockTx
{

    protected override void GetTransactions(PurchaseOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("Stock")
                .Dim("Item", line.Item).Dim("Cell", document.Location).Res("Qty", line.Quantity));
        }
    }
}
