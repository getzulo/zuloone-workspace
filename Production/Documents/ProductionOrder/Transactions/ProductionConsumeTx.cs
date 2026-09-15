#nullable enable
using System;

// Output writes off components from stock: −quantity from the cell. Stock is
// one-sided, there is no counter-leg. A shortage is rejected in
// ProductionOrderEventHandler.OnBeforePostAsync.
//
// The register gets BaseQuantity (the item's base unit, computed by the platform
// on line save); zero = "unit not specified, no conversion" → the entered
// QtyRequired is the base — that is how BOM-expanded lines arrive: BomService
// already returns demand in the component's stock unit.
//
// There is NO local rounding here any more: the value arrives already rounded
// to the unit's own precision (UnitOfMeasure.DecimalPlaces), and the old
// RoundQuantity rounded a second time under a different setting — two
// disagreeing roundings were the bug.
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
