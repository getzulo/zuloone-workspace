#nullable enable
using System;

// Shipment on the invoice: −quantity from the sale cell. Stock is single-entry,
// there is no counter-leg. Over-sale protection lives in
// SalesInvoiceEventHandler.OnBeforePostAsync: the engine does not check, because
// the register allows a negative balance.
//
// The register gets BaseQuantity (the item's base unit, computed by the platform
// when the line is saved); zero = "unit not specified, no conversion" → the
// entered quantity is the base. The invoice's money legs (Receivable/Revenue/VAT)
// stay on the ENTERED Quantity: the invoice sold 5 boxes at a per-box price.
public partial class SalesStockTx
{

    protected override void GetTransactions(SalesInvoice document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            transactions.Add(new RegisterMovementSpec("Stock")
                .Dim("Item", line.Item).Dim("Cell", document.Location).Res("Qty", -qty));
        }
    }
}
