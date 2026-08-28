#nullable enable
using System;
using ZuloOne.Services.Contracts;

// Выпуск списывает компоненты со склада — сбалансированная ПАРА двойной записи
// Stock: компонент уходит с ячейки во «внешний мир» (External). Количество
// округляется общим MeasurementService. Нехватка отклоняется в
// ProductionOrderEventHandler.OnBeforePostAsync.
public partial class ProductionConsumeTx
{
    private static readonly Guid External = Guid.Parse("e0000000-0000-4000-8000-0000000000e1");

    protected override void GetTransactions(ProductionOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var measure = GetService<IMeasurementService>();
        foreach (var line in document.Components)
        {
            var q = measure.RoundQuantity(line.QtyRequired);
            transactionPairs.Add(
                new RegisterMovementSpec("Stock").Dim("Item", line.Component).Dim("Cell", document.Location).Res("Qty", -q),
                new RegisterMovementSpec("Stock").Dim("Item", line.Component).Dim("Cell", External).Res("Qty", q));
        }
    }
}
