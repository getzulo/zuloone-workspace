#nullable enable
using System;
using System.Linq;
using ZuloOne.Services.Contracts;

// Себестоимость: оприходование заказа поставщику наполняет регистр стоимости
// запасов — приход по каждой строке даёт +Value (сумма строки из PricingService
// плюс доля NonRecoverableVat) и +Qty по товару. Средняя себестоимость товара
// = Value / Qty (в отчётах). Скрипт живёт в Costing и цепляется к подтипу
// PurchaseOrder.Received.
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
        var extra = document.NonRecoverableVat;
        var nets = document.Lines.Select(l => pricing.LineAmount(l.Quantity, l.UnitPrice)).ToList();
        var total = nets.Sum();
        var leftover = extra;
        var scale = GlobalConstants.Get<int?>("AmountScale") ?? 2;
        for (var i = 0; i < document.Lines.Count; i++)
        {
            var line = document.Lines[i];
            var share = extra == 0m || total == 0m ? 0m
                : i == document.Lines.Count - 1
                    ? leftover
                    : Math.Round(extra * nets[i] / total, scale, MidpointRounding.AwayFromZero);
            leftover -= share;
            transactions.Add(new RegisterMovementSpec("InventoryValue")
                .An(Analytics.InventoryValue.Item, line.Item)
                .Res("Value", nets[i] + share)
                .Res("Qty", line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity));
        }
    }
}
