#nullable enable
using ZuloOne.Services.Contracts;

// Себестоимость FIFO: оприходование заказа поставщику создаёт партию (лот) в
// регистре ItemCostFifo — по каждой строке +Quantity и +Amount. Регистр с
// движком FIFO хранит слои по товару; при расходе (−Quantity, Amount = 0) движок
// сам считает себестоимость выбытия по старейшим лотам и отклоняет перерасход.
// Движение типизированное: Item — физическое измерение регистра, не аналитика.
// Сумма лота — общий PricingService.
//
// ═══ ЕДИНСТВЕННОЕ МЕСТО В ДЕРЕВЕ, ГДЕ ДВЕ КОНВЕНЦИИ ВСТРЕЧАЮТСЯ В ОДНОМ
// ОПЕРАТОРЕ, И ДВА АРГУМЕНТА НАМЕРЕННО ЧИТАЮТ РАЗНЫЕ ПОЛЯ ОДНОЙ СТРОКИ:
//
//   Quantity ← BaseQuantity (БАЗОВАЯ единица товара). Это КОЛИЧЕСТВО НА СКЛАДЕ:
//     слои FIFO списываются расходом, который придёт из складских проводок, а те
//     тоже в базовой единице. Возьми здесь введённые «5 ящиков» — и лот из 5
//     встретится с расходом в 60 штук: движок объявит перерасход на товаре,
//     который физически лежит на полке.
//
//   Amount ← Quantity (ВВЕДЁННОЕ количество) × UnitPrice. Это ДЕНЬГИ, и цена
//     задана ЗА ТУ ЖЕ ЕДИНИЦУ, в которой введено количество: 5 ящиков × цена за
//     ящик. Пересчитай и здесь — сумма лота вырастет ровно в коэффициент
//     упаковки, и себестоимость запаса разойдётся со счётом поставщика.
//
// Итог: Amount/Quantity даёт цену за БАЗОВУЮ единицу — ровно то, чем FIFO должен
// оценивать выбытие. Ноль в BaseQuantity значит «единица строки не указана,
// пересчёта не было» — тогда введённое количество и есть базовое, и оба
// аргумента честно совпадают.
//
// Своего округления количества здесь больше нет: значение приходит округлённым
// по точности самой единицы. RoundAmount/RoundWeight в других скриптах остаются —
// убран только второй путь округления КОЛИЧЕСТВА.
public partial class ReceiptFifoTx
{
    protected override void GetTransactions(PurchaseOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        foreach (var line in document.Lines)
        {
            transactions.Add(new ItemCostFifo
            {
                Item = line.Item,
                Quantity = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity,
                Amount = pricing.LineAmount(line.Quantity, line.UnitPrice)
            });
        }
    }
}
