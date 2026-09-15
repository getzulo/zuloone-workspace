#nullable enable
using System;

// Оприходование заказа поставщику: +количество на принимающую ячейку. Stock —
// односторонний накопительный регистр, поэтому встречной ноги нет, а остаток
// ячейки и есть фактическое наличие.
//
// В регистр уходит BaseQuantity (базовая единица товара, считает платформа при
// сохранении строки); ноль = «единица не указана, пересчёта не было» → введённое
// количество и есть базовое. Заказ на 5 ящиков приходует 60 штук, а НЕ 5.
public partial class GoodsReceiptStockTx
{

    protected override void GetTransactions(PurchaseOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            transactions.Add(new RegisterMovementSpec("Stock")
                .Dim("Item", line.Item).Dim("Cell", document.Location).Res("Qty", qty));
        }
    }
}
