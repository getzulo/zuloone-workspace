#nullable enable
using ZuloOne.Services.Contracts;

public partial class SalesReturnRevenueTx
{
    protected override void GetTransactions(SalesReturn document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        foreach (var line in document.Lines)
        {
            var amount = pricing.LineAmount(line.Quantity, line.UnitPrice);
            if (amount == 0m) continue;
            transactions.Add(new RegisterMovementSpec("Revenue")
                .An(Analytics.Revenue.Item, line.Item)
                .An(Analytics.Revenue.Customer, document.Customer)
                .An(Analytics.Revenue.SalesContract, document.Contract)
                .Res("Amount", -amount));
        }
    }
}
