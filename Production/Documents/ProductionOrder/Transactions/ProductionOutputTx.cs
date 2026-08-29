#nullable enable
using System;

// Выпуск приходует готовое изделие: +количество на ячейку выпуска. Stock
// односторонний, встречной ноги нет.
//
// Количество выпуска объявлено с пересчётом В ШАПКЕ (Quantity + Unit →
// BaseQuantity по Product.UnitOfMeasure): изделие можно заказать «2 паллеты», а
// на склад лечь должно столько штук, сколько в паллете. Ноль = «единица не
// указана, пересчёта не было» → введённое количество и есть базовое.
//
// Своего округления здесь БОЛЬШЕ НЕТ: значение приходит округлённым по точности
// самой единицы (UnitOfMeasure.DecimalPlaces), а прежний RoundQuantity округлял
// второй раз и по другой настройке — два спорящих округления и есть баг.
public partial class ProductionOutputTx
{

    protected override void GetTransactions(ProductionOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var qty = document.BaseQuantity != 0m ? document.BaseQuantity : document.Quantity;
        transactions.Add(new RegisterMovementSpec("Stock")
            .Dim("Item", document.Product).Dim("Cell", document.Location).Res("Qty", qty));
    }
}
