#nullable enable

// Transfer between cells — TWO single Stock movements (single-entry):
// outcome −qty at FromCell, income +qty at ToCell. Goods are neither created
// nor destroyed — they just move between cells.
//
// The register gets BaseQuantity (the item's base unit, computed by the platform
// when the line is saved); zero = "unit not specified, no conversion" → the
// entered quantity is the base. Both legs take ONE value — otherwise a transfer
// would create or destroy goods.
public partial class StockTransferTx
{
    protected override void GetTransactions(StockTransfer document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            transactions.Add(new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.FromCell).Res("Qty", -qty));
            transactions.Add(new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.ToCell).Res("Qty", qty));
        }
    }
}
