#nullable enable
using ZuloOne.Services.Contracts;

// Ukraine VAT on invoice issue — accrual into this pack's UaVatPayable.
// Rate comes from TaxRateApplied (tax contour on document date), not a flat constant.
// Zero rate = contour not configured = no posting. Sales is not edited.
public partial class UaVatTx
{
    protected override void GetTransactions(SalesRealization document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();
        var tax = GetService<ITaxService>();
        var rate = document.TaxRateApplied;
        if (rate <= 0m) return;

        decimal baseAmount = 0m;
        foreach (var line in document.Lines)
            baseAmount += pricing.LineAmount(line.Quantity, line.UnitPrice, document.DiscountPercent);

        var vat = tax.CalculateTax(baseAmount, rate);
        if (vat > 0m)
        {
            transactions.Add(new RegisterMovementSpec("UaVatPayable")
                .An(Analytics.UaVatPayable.Customer, document.Customer)
                .An(Analytics.UaVatPayable.SalesContract, document.Contract)
                .Res("Amount", vat));
        }
    }
}
