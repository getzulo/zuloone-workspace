#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// CRM MODEL extension of the sales invoice: on issue the customer earns points.
// Line amount comes from the shared PricingService so points, revenue and VAT
// are computed from ONE base. The script lives in CRM and attaches to the
// SalesInvoice.Issued subtype — the engine runs it in the posting chain.
//
// RATE AND SWITCH — A SETTING, NOT A CONSTANT IN CODE. CRMSettings declares
// PointsPerCurrencyUnit and LoyaltyEnabled; until then neither was read by
// a single line — the rate was hard-coded 1:1, and «turn loyalty off»
// turned nothing off.
//
// Settings are read ONCE in the field initializer, not inside GetTransactions:
// the script instance lives for one posting, so this is «fresh settings on
// every posting», and the DB call happens before the register connection is
// opened (the same trick as in CostingValuationTotalDriver).
//
// COMPATIBILITY AND THE OPTIONAL-BOOLEAN TRAP. LoyaltyEnabled is declared
// optional, and an optional Boolean on the platform is NOT nullable:
// «unset» is indistinguishable from «off». CRMSettings records were created
// before the flag started being read at all, so every existing one is false.
// Trust it literally and the change would silently turn loyalty off on every
// stand where someone once opened and saved the CRM settings form — with no
// error in the log.
//
// So the sign that «the module is configured» is NOT the flag, but a
// positive rate:
//   no record at all            → work as before, 1 point per currency unit;
//   record exists, rate unset   → loyalty was never configured, also as before;
//   record exists, rate is set  → configured on purpose; flag and rate beat the code.
// That way the switch really turns things off, but only for whoever flipped
// it on purpose, not for everyone else.
public partial class SalesLoyaltyTx
{
    private readonly (bool Enabled, decimal Rate) _loyalty = ReadLoyaltySettings();

    private static (bool Enabled, decimal Rate) ReadLoyaltySettings()
    {
        var rows = GetService<IDictionaryManager>()
            .GetRecordsAsync<CRMSettings>(null, 1).GetAwaiter().GetResult();
        if (rows.Count == 0) return (true, 1m);

        var rate = rows[0].PointsPerCurrencyUnit;
        if (rate <= 0m) return (true, 1m);

        return (rows[0].LoyaltyEnabled, rate);
    }

    protected override void GetTransactions(SalesInvoice document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        if (!_loyalty.Enabled) return;

        var pricing = GetService<IPricingService>();

        decimal amount = 0m;
        foreach (var line in document.Lines)
            amount += pricing.LineAmount(line.Quantity, line.UnitPrice, document.DiscountPercent);

        var points = Math.Round(amount * _loyalty.Rate, 2, MidpointRounding.AwayFromZero);

        if (points > 0m)
            transactions.Add(new RegisterMovementSpec("LoyaltyPoints")
                .Dim("Customer", document.Customer)
                .Res("Points", points));
    }
}
