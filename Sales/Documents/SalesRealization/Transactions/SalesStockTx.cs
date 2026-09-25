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

    protected override void GetTransactions(SalesRealization document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var holds = GetService<ISalesFulfillmentService>()
            .PickHoldsAsync(document.SourceOrder).GetAwaiter().GetResult();

        foreach (var line in document.Lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            transactions.Add(new RegisterMovementSpec("Stock")
                .Dim("Item", line.Item).Dim("Cell", document.Location).Res("Qty", -qty));

            // Доля, которую держал отбор, на этом счёте не висела: переход в
            // Issued её сам не снимет. Снимаем не больше строки — волна делит
            // одно задание между заказами.
            var need = qty;
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
