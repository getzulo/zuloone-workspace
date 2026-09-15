using System.Linq;
using ZuloOne.Managers;

// «Списать баллы»: баланс LoyaltyPoints и лимит уровня. Отдельного сервиса
// лояльности нет — те же ITotalsManager и LoyaltyTier, что в OnBeforePost.
public partial class RedeemLoyaltyCommand
{
    public override async Task ExecuteAsync(LoyaltyRedemption document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<LoyaltyRedemption>(document.MetaId);
        if (full == null) return;

        if (full.Points <= 0m)
        {
            context.AddClientAction(ClientAction.Message("Укажите число баллов больше нуля."));
            return;
        }

        var movements = context.GetService<ITotalsManager>();
        var balance = await movements.GetBalanceAsync("LoyaltyPoints",
            new Dictionary<string, object?> { ["Customer"] = full.Customer });
        var available = balance != null && balance.TryGetValue("Points", out var raw) && raw != null
            ? Convert.ToDecimal(raw)
            : 0m;

        if (full.Points > available)
        {
            context.AddClientAction(ClientAction.Message(
                $"На счету {available} баллов — списать {full.Points} нельзя."));
            return;
        }

        var tiers = context.GetService<IDictionaryManager<LoyaltyTier>>();
        var reached = (await tiers.GetRecordsAsync($"MinPoints <= {available}"))
            .OrderByDescending(t => t.MinPoints)
            .FirstOrDefault();
        if (reached == null)
        {
            if ((await tiers.GetRecordsAsync(null)).Any())
            {
                context.AddClientAction(ClientAction.Message(
                    $"Баланса {available} не хватает ни на один уровень лояльности."));
                return;
            }
        }
        else if (full.Points > reached.MaxRedemptionPerDocument)
        {
            context.AddClientAction(ClientAction.Message(
                $"Уровень «{reached.Name}» позволяет списать не больше {reached.MaxRedemptionPerDocument} баллов за раз."));
            return;
        }

        full.Subtype = LoyaltyRedemption.Subtypes.Redeemed;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Баллы списаны."));
    }
}
