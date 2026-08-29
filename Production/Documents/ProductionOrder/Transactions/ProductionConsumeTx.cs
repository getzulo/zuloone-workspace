#nullable enable
using System;

// Выпуск списывает компоненты со склада: −количество с ячейки. Stock
// односторонний, встречной ноги нет. Нехватка отклоняется в
// ProductionOrderEventHandler.OnBeforePostAsync.
//
// В регистр уходит BaseQuantity (базовая единица товара, считает платформа при
// сохранении строки); ноль = «единица не указана, пересчёта не было» → введённое
// QtyRequired и есть базовое — именно так приходят строки, развёрнутые из
// спецификации: BomService уже отдаёт потребность в складской единице компонента.
//
// Своего округления здесь БОЛЬШЕ НЕТ: значение приходит округлённым по точности
// самой единицы (UnitOfMeasure.DecimalPlaces), а прежний RoundQuantity округлял
// второй раз и по другой настройке — два спорящих округления и есть баг.
public partial class ProductionConsumeTx
{

    protected override void GetTransactions(ProductionOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Components)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.QtyRequired;
            transactions.Add(new RegisterMovementSpec("Stock")
                .Dim("Item", line.Component).Dim("Cell", document.Location).Res("Qty", -qty));
        }
    }
}
