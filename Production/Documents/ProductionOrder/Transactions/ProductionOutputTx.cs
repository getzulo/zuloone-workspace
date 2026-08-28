#nullable enable
using System;
using ZuloOne.Services.Contracts;

// Выпуск приходует готовое изделие — сбалансированная ПАРА двойной записи Stock:
// изделие приходит из «внешнего мира» (External) на ячейку выпуска. Количество
// округляется общим MeasurementService.
public partial class ProductionOutputTx
{
    private static readonly Guid External = Guid.Parse("e0000000-0000-4000-8000-0000000000e1");

    protected override void GetTransactions(ProductionOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var measure = GetService<IMeasurementService>();
        var q = measure.RoundQuantity(document.Quantity);
        transactionPairs.Add(
            new RegisterMovementSpec("Stock").Dim("Item", document.Product).Dim("Cell", External).Res("Qty", -q),
            new RegisterMovementSpec("Stock").Dim("Item", document.Product).Dim("Cell", document.Location).Res("Qty", q));
    }
}
