#nullable enable
using System.Linq;

namespace ZuloOne.Runtime.Generated;

// Point-redemption rules. The register itself will not go negative, but the
// engine's refusal is a posting exception with no clear reason; here the
// transition is rejected with a meaningful text, and what the register does
// not know is added: the tier ladder.
//
// The tier is NOT stored on the customer; it is derived from the accumulated
// balance — so it cannot drift from actual points. The tier decides how many
// points one document may redeem.
//
// Why the checks live in the event, not the transactional script: both the
// register balance and the tier dictionary are read asynchronously, and
// GetTransactions is synchronous. This is deliberately not a service — a
// script of the same model cannot see that model's I<Service> contract
// (contracts are assembled after its scripts).
public partial class LoyaltyRedemptionEventHandler : TypedDocumentEventHandler<LoyaltyRedemption>
{

    public override async Task<EventResult> OnBeforePostAsync(LoyaltyRedemption header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        if (header.Subtype != "Redeemed") return EventResult.Ok();

        var requested = header.Points;
        if (requested <= 0m)
            return EventResult.Cancel("Списывать нечего: количество баллов должно быть положительным.");

        var movements = context.GetService<ITotalsManager>();
        var balance = await movements.GetBalanceAsync("LoyaltyPoints",
            new Dictionary<string, object?> { ["Customer"] = header.Customer });
        var available = balance != null && balance.TryGetValue("Points", out var raw) && raw != null
            ? Convert.ToDecimal(raw)
            : 0m;

        if (requested > available)
            return EventResult.Cancel($"На счету {available} баллов — списать {requested} нельзя.");

        var tiers = context.GetService<IDictionaryManager<LoyaltyTier>>();
        var reached = (await tiers.GetRecordsAsync($"MinPoints <= {available}"))
            .OrderByDescending(t => t.MinPoints)
            .FirstOrDefault();

        // An empty ladder — tiers are not set up yet; then there are no limits
        // and redemption is constrained only by the balance. Otherwise the
        // customer must have reached a tier.
        if (reached == null)
        {
            var anyTier = (await tiers.GetRecordsAsync(null)).Any();
            if (anyTier)
                return EventResult.Cancel($"Баланса {available} не хватает ни на один уровень лояльности.");
            return EventResult.Ok();
        }

        if (requested > reached.MaxRedemptionPerDocument)
            return EventResult.Cancel(
                $"Уровень «{reached.Name}» позволяет списать не больше {reached.MaxRedemptionPerDocument} баллов за раз.");

        return EventResult.Ok();
    }
}
