#nullable enable

// Storage → picking: TWO single Stock movements per line (single-entry) —
// minus from the storage cell (FromCell, header), plus into the picking cell (ToCell, line).
//
// The register gets BaseQuantity (the item's base unit, computed by the platform
// when the line is saved); zero = "unit not specified, no conversion" → the
// entered quantity is the base. Both legs take ONE value.
public partial class PickTaskTx
{
    protected override void GetTransactions(PickTask document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            transactions.Add(new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.FromCell).Res("Qty", -qty));
            transactions.Add(new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", line.ToCell).Res("Qty", qty));
        }
    }
}
