#nullable enable
using ZuloOne.Services.Contracts;

// Recognizes revenue per line by item and customer. Line amount is computed
// by the shared PricingService (quantity × price, rounded to money precision).
public partial class SalesRevenueTx
{
    protected override void GetTransactions(SalesInvoice document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("Revenue")
                .An(Analytics.Revenue.Item, line.Item)
                .An(Analytics.Revenue.Customer, document.Customer)
                .Res("Amount", pricing.LineAmount(line.Quantity, line.UnitPrice, document.DiscountPercent)));
        }
    }
}
