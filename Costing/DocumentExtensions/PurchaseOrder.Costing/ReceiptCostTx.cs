#nullable enable
using ZuloOne.Services.Contracts;

// Costing: receiving a purchase order fills the inventory-value
// register — each line's receipt gives +Value (line amount from
// PricingService) and +Qty by item. Average item cost = Value / Qty
// (in reports). The script lives in Costing and attaches to
// PurchaseOrder.Received.
//
// The same convention split as in ReceiptFifoTx, in one operator: Value
// is on the ENTERED quantity (price is for the entered unit: 5 boxes ×
// price per box), Qty is on the BASE. Otherwise Value/Qty would be a
// per-box price multiplied by a Stock balance counted in pieces — inventory
// valuation would drift by exactly the pack factor.
public partial class ReceiptCostTx
{
    protected override void GetTransactions(PurchaseOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("InventoryValue")
                .An(Analytics.InventoryValue.Item, line.Item)
                .Res("Value", pricing.LineAmount(line.Quantity, line.UnitPrice))
                .Res("Qty", line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity));
        }
    }
}
