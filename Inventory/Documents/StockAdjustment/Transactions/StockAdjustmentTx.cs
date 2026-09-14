#nullable enable
using System;

// Stock adjustment — a SINGLE Stock movement (single-entry, like warehouse in
// MIQS): surplus (Quantity > 0) adds qty to the cell, shortage (Quantity < 0)
// writes it off. Register balance = actual on-hand, no "External" counterparty.
// Guard against writing off into a minus — in StockAdjustmentEventHandler.OnBeforePostAsync.
//
// The register gets BaseQuantity (the item's base unit, computed by the platform
// when the line is saved); zero = "unit not specified, no conversion" → the
// entered quantity is the base. The sign is kept through conversion, so a
// shortage stays a shortage.
public partial class StockAdjustmentTx
{
    protected override void GetTransactions(StockAdjustment document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            transactions.Add(
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.Cell).Res("Qty", qty));
        }
    }
}
