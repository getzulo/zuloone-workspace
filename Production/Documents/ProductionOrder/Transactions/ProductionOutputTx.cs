#nullable enable
using System;
using ZuloOne.Services.Contracts;

// Выпуск приходует готовое изделие: +количество на ячейку выпуска. Stock
// односторонний, встречной ноги нет. Количество округляется общим
// MeasurementService.
public partial class ProductionOutputTx
{

    protected override void GetTransactions(ProductionOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var measure = GetService<IMeasurementService>();
        var q = measure.RoundQuantity(document.Quantity);
        transactions.Add(new RegisterMovementSpec("Stock")
            .Dim("Item", document.Product).Dim("Cell", document.Location).Res("Qty", q));
    }
}
