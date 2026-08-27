#nullable enable
using ZuloOne.Services.Contracts;

// Себестоимость FIFO: оприходование заказа поставщику создаёт партию (лот) в
// регистре ItemCostFifo — по каждой строке +Quantity и +Amount. Регистр с
// движком FIFO хранит слои по товару; при расходе (−Quantity, Amount = 0) движок
// сам считает себестоимость выбытия по старейшим лотам и отклоняет перерасход.
// Движение типизированное: Item — физическое измерение регистра, не аналитика.
// Сумма лота — общий PricingService, количество округляет MeasurementService:
// оба сервиса лежат ниже (Inventory/Measurement), точность в одной настройке.
public partial class ReceiptFifoTx
{
    protected override void GetTransactions(PurchaseOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        var measure = GetService<IMeasurementService>();
        foreach (var line in document.Lines)
        {
            transactions.Add(new ItemCostFifo
            {
                Item = line.Item,
                Quantity = measure.RoundQuantity(line.Quantity),
                Amount = pricing.LineAmount(line.Quantity, line.UnitPrice)
            });
        }
    }
}
