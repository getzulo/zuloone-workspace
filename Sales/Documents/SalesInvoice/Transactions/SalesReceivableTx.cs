#nullable enable
using ZuloOne.Services.Contracts;

// Receivable on an issued invoice: the customer owes the invoice amount.
// The script is bound to the Issued subtype — that is the whole settlement
// mechanic: on "Issued → Paid" the engine lifts Issued-state movements, the
// debt disappears on its own, no separate reversing movement is needed. Revenue
// and the warehouse write-off stay: their scripts are bound to the DOCUMENT,
// not the subtype.
public partial class SalesReceivableTx
{
    protected override void GetTransactions(SalesInvoice document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("Receivable")
                .An(Analytics.Receivable.Customer, document.Customer)
                .Res("Amount", pricing.LineAmount(line.Quantity, line.UnitPrice, document.DiscountPercent)));
        }
    }
}
