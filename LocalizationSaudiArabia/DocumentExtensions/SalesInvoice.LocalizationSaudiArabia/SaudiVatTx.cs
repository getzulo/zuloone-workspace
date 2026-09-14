#nullable enable
using ZuloOne.Services.Contracts;

// Saudi Arabia VAT on invoice issue — accrual into the localization's
// OWN VatPayable register.
//
// WHY THIS SCRIPT LIVES HERE, NOT IN SALES. It used to sit in the Sales
// model under the name SalesVatTx — meaning country logic was written into
// the universal sales module. The compiler did not catch this: both the
// rate constant and the register are addressed by STRINGS
// (`GlobalConstants.Get("SaudiVatRate")`, `RegisterMovementSpec("VatPayable")`),
// and inter-model dependency checks work on types. The layer was pushed
// exactly where the platform has no control, and for a customer in another
// country this code still ran on every invoice — silently yielding zero
// because they have no Saudi constant.
//
// Now the owner is the localization model: it depends on Sales (not the
// other way around), and its objects ship with the country package. The
// invoice itself is untouched: the script is in the type pool, and Issued
// is attached by a checkbox (the binding row).
//
// This does NOT cancel the universal contour: the same tax independently
// lands in Tax.TaxLedger via TaxCalculation, which the invoice event
// produces by determination rules. Here — a country slice for ZATCA
// reporting.
//
// Line base is the shared PricingService, the tax ITSELF is
// TaxService.CalculateTax (base × rate with rounding): tax calculation
// lives in the tax service, not smeared across postings.
//
// THE RATE IS TAKEN FROM THE DOCUMENT, not from a constant. Previously
// this was `GlobalConstants.Get<decimal>("SaudiVatRate")` — a flat 0.15
// WITH NO DATE, a second source of truth next to the dated TaxRate
// dictionary. While the rate did not change, there was no difference; on
// the day of the change TaxLedger would go by the new rate and VatPayable
// would stay on the old one, and they would drift silently. A back-dated
// invoice would be computed here at today's rate, and in the universal
// contour — at the rate that was in force.
//
// Now `TaxRateApplied` pins on the invoice the rate picked by the tax
// contour on the document date (SalesInvoiceEventHandler.OnBeforePost).
// Zero means the tax contour is not configured — then there is simply no
// posting, same as before when the constant was missing.
public partial class SaudiVatTx
{
    protected override void GetTransactions(SalesInvoice document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
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
            transactions.Add(new RegisterMovementSpec("VatPayable")
                .An(Analytics.VatPayable.Customer, document.Customer)
                .Res("Amount", vat));
    }
}
