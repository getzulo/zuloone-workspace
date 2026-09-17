#nullable enable
using ZuloOne.Services.Contracts;

// Receivable on an issued invoice: net, plus VAT when TaxRateApplied is set.
// TaxCalculation no longer adds a second Receivable leg (it doubled the tax).
public partial class SalesReceivableTx
{
    protected override void GetTransactions(SalesRealization document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        decimal net = 0m;
        foreach (var line in document.Lines)
            net += pricing.LineAmount(line.Quantity, line.UnitPrice, document.DiscountPercent);
        if (net == 0m) return;

        var amount = net;
        if (document.TaxRateApplied > 0m)
            amount += GetService<ITaxService>().CalculateTax(net, document.TaxRateApplied);

        transactions.Add(new RegisterMovementSpec("Receivable")
            .An(Analytics.Receivable.Customer, document.Customer)
            .An(Analytics.Receivable.SalesContract, document.Contract)
            .Res("Amount", amount));
    }
}
