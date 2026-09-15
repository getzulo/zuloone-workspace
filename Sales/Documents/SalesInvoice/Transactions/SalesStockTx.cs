#nullable enable
using System;

// Отгрузка по счёту: −количество с ячейки продажи. Stock односторонний, встречной
// ноги нет. Защита от перепродажи — в событии
// SalesInvoiceEventHandler.OnBeforePostAsync: движковой проверки нет, потому что
// регистр допускает отрицательный остаток.
//
// В регистр уходит BaseQuantity (базовая единица товара, считает платформа при
// сохранении строки); ноль = «единица не указана, пересчёта не было» → введённое
// количество и есть базовое. Денежные ноги счёта (Receivable/Revenue/VAT) при
// этом остаются на ВВЕДЁННОМ Quantity: в счёте продано 5 ящиков по цене за ящик.
public partial class SalesStockTx
{

    protected override void GetTransactions(SalesInvoice document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            transactions.Add(new RegisterMovementSpec("Stock")
                .Dim("Item", line.Item).Dim("Cell", document.Location).Res("Qty", -qty));
        }
    }
}
