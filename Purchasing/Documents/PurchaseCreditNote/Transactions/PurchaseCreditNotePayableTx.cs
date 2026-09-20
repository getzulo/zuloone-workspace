#nullable enable
using ZuloOne.Services.Contracts;

public partial class PurchaseCreditNotePayableTx
{
    protected override void GetTransactions(PurchaseCreditNote document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var rate = document.TaxRateApplied;
        if (rate <= 0m) return;

        var pricing = GetService<IPricingService>();
        var tax = GetService<ITaxService>();
        decimal baseAmount = 0m;
        foreach (var line in document.Lines)
            baseAmount += pricing.LineAmount(line.Quantity, line.UnitPrice);

        var vat = tax.CalculateTax(baseAmount, rate);
        if (vat == 0m) return;

        transactions.Add(new RegisterMovementSpec("Payable")
            .An(Analytics.Payable.Supplier, document.Supplier)
            .Res("Amount", -vat));
    }
}
