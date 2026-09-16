#nullable enable
using ZuloOne.Services.Contracts;

public partial class PurchaseReturnPayableTx
{
    protected override void GetTransactions(PurchaseReturn document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        foreach (var line in document.Lines)
        {
            var amount = pricing.LineAmount(line.Quantity, line.UnitPrice);
            if (amount == 0m) continue;
            transactions.Add(new RegisterMovementSpec("Payable")
                .An(Analytics.Payable.Supplier, document.Supplier)
                .Res("Amount", -amount));
        }
    }
}
