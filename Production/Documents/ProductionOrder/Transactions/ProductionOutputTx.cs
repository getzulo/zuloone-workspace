#nullable enable
using System;

// Output receipts the finished good: +quantity onto the output cell. Stock is
// one-sided, there is no counter-leg.
//
// Output quantity is declared with conversion ON THE HEADER (Quantity + Unit →
// BaseQuantity by Product.UnitOfMeasure): the good can be ordered as "2 pallets",
// but stock must receive as many pieces as a pallet holds. Zero = "unit not
// specified, no conversion" → the entered quantity is the base.
//
// There is NO local rounding here any more: the value arrives already rounded
// to the unit's own precision (UnitOfMeasure.DecimalPlaces), and the old
// RoundQuantity rounded a second time under a different setting — two
// disagreeing roundings were the bug.
public partial class ProductionOutputTx
{

    protected override void GetTransactions(ProductionOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var qty = document.BaseQuantity != 0m ? document.BaseQuantity : document.Quantity;
        transactions.Add(new RegisterMovementSpec("Stock")
            .Dim("Item", document.Product).Dim("Cell", document.Location).Res("Qty", qty));
    }
}
