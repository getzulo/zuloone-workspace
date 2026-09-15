#nullable enable
using System;

// Receipting a purchase order: +quantity onto the receiving cell. Stock is a
// one-sided accumulation register, so there is no counter-leg, and the cell
// balance is physical on-hand.
//
// The register gets BaseQuantity (the item's base unit, computed by the platform
// on line save); zero = "unit not specified, no conversion" → the entered
// quantity is the base. An order for 5 boxes receipts 60 pieces, NOT 5.
public partial class GoodsReceiptStockTx
{

    protected override void GetTransactions(PurchaseOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            transactions.Add(new RegisterMovementSpec("Stock")
                .Dim("Item", line.Item).Dim("Cell", document.Location).Res("Qty", qty));
        }
    }
}
