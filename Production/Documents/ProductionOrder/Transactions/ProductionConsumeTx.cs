#nullable enable
using System;
using ZuloOne.Services.Contracts;

// Выпуск списывает компоненты со склада: −количество с ячейки. Stock
// односторонний, встречной ноги нет. Количество округляется общим
// MeasurementService. Нехватка отклоняется в
// ProductionOrderEventHandler.OnBeforePostAsync.
public partial class ProductionConsumeTx
{

    protected override void GetTransactions(ProductionOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var measure = GetService<IMeasurementService>();
        foreach (var line in document.Components)
        {
            var q = measure.RoundQuantity(line.QtyRequired);
            transactions.Add(new RegisterMovementSpec("Stock")
                .Dim("Item", line.Component).Dim("Cell", document.Location).Res("Qty", -q));
        }
    }
}
