#nullable enable
using ZuloOne.Services.Contracts;

// Себестоимость: оприходование заказа поставщику наполняет регистр стоимости
// запасов — приход по каждой строке даёт +Value (сумма строки из PricingService)
// и +Qty по товару. Средняя себестоимость товара = Value / Qty (в отчётах).
// Скрипт живёт в Costing и цепляется к подтипу PurchaseOrder.Received.
//
// Тот же разрыв конвенций, что и в ReceiptFifoTx, в одном операторе: Value — на
// ВВЕДЁННОМ количестве (цена задана за введённую единицу: 5 ящиков × цена за
// ящик), Qty — на БАЗОВОМ. Иначе Value/Qty дало бы цену за ящик, а умножалась бы
// она на остаток Stock, который считается в штуках, — оценка запаса разъехалась бы
// ровно в коэффициент упаковки.
public partial class ReceiptCostTx
{
    protected override void GetTransactions(PurchaseOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("InventoryValue")
                .An(Analytics.InventoryValue.Item, line.Item)
                .Res("Value", pricing.LineAmount(line.Quantity, line.UnitPrice))
                .Res("Qty", line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity));
        }
    }
}
