#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Award loyalty points on issue. A live LoyaltyCampaign overlays the
// course for its window. Otherwise a reached LoyaltyTier with a positive
// EarnRate overlays CRMSettings. The Boolean trap on LoyaltyEnabled stays.
//
// Settings are read once in the field initializer: the script instance lives
// one posting. Tier cannot live there — it depends on document.Customer.
//
// OPTIONAL BOOLEAN TRAP. LoyaltyEnabled is non-nullable: unset == false.
// Existing CRMSettings rows were saved before the flag was read, so they are
// all false. Trust the flag literally and every stand that once opened the
// settings form silently stops awarding points. A positive PointsPerCurrencyUnit
// is the "configured" signal; missing/zero rate keeps the legacy 1:1.
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

    private static decimal EffectiveRate(Guid customer, decimal fallback, DateTime onDate)
    {
        var campaign = GetService<ILoyaltyCampaignService>()
            .EarnRateOfAsync(onDate).GetAwaiter().GetResult();
        if (campaign > 0m) return campaign;

        if (customer == Guid.Empty) return fallback;

        var balance = GetService<ITotalsManager>()
            .GetBalanceAsync("LoyaltyPoints",
                new Dictionary<string, object?> { ["Customer"] = customer })
            .GetAwaiter().GetResult();
        var points = balance != null && balance.TryGetValue("Points", out var raw) && raw != null
            ? Convert.ToDecimal(raw)
            : 0m;

        var reached = GetService<IDictionaryManager<LoyaltyTier>>()
            .GetRecordsAsync($"MinPoints <= {points}")
            .GetAwaiter().GetResult()
            .OrderByDescending(t => t.MinPoints)
            .FirstOrDefault();
        if (reached != null && reached.EarnRate > 0m)
            return reached.EarnRate;
        return fallback;
    }

    protected override void GetTransactions(
        SalesRealization document,
        TransactionPairCollection transactionPairs,
        TransactionCollection transactions)
    {
        if (!_loyalty.Enabled) return;

        var pricing = GetService<IPricingService>();
        decimal amount = 0m;
        foreach (var line in document.Lines)
            amount += pricing.LineAmount(line.Quantity, line.UnitPrice, document.DiscountPercent);

        var onDate = document.DocumentDate == default
            ? DateTime.UtcNow.Date
            : document.DocumentDate.Date;
        var points = Math.Round(
            amount * EffectiveRate(document.Customer, _loyalty.Rate, onDate),
            2, MidpointRounding.AwayFromZero);
        if (points > 0m)
            transactions.Add(new RegisterMovementSpec("LoyaltyPoints")
                .Dim("Customer", document.Customer)
                .Res("Points", points));
    }
}
