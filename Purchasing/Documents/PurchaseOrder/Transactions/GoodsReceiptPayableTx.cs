#nullable enable
using ZuloOne.Services.Contracts;

// Recognizes payables to the vendor per line (amount — shared PricingService,
// quantity × price), sliced by supplier.
public partial class GoodsReceiptPayableTx
{
    protected override void GetTransactions(PurchaseOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("Payable")
                .An(Analytics.Payable.Supplier, document.Supplier)
                .Res("Amount", pricing.LineAmount(line.Quantity, line.UnitPrice)));
        }
    }
}
