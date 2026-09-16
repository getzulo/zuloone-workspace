#nullable enable
using ZuloOne.Services.Contracts;

public partial class SaudiCreditVatTx
{
    protected override void GetTransactions(SalesCreditNote document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        var tax = GetService<ITaxService>();
        var rate = document.TaxRateApplied;
        if (rate <= 0m) return;

        decimal baseAmount = 0m;
        foreach (var line in document.Lines)
            baseAmount += pricing.LineAmount(line.Quantity, line.UnitPrice);

        var vat = tax.CalculateTax(baseAmount, rate);
        if (vat > 0m)
        {
            transactions.Add(new RegisterMovementSpec("VatPayable")
                .An(Analytics.VatPayable.Customer, document.Customer)
                .An(Analytics.VatPayable.SalesContract, document.Contract)
                .Res("Amount", -vat));
        }
    }
}
