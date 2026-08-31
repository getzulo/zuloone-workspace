#nullable enable
using System.Linq;

namespace ZuloOne.Runtime.Generated;

// Правила погашения баллов. Регистр сам не даёт уйти в минус, но отказ движка —
// это исключение проведения без внятной причины; здесь переход отклоняется
// осмысленным текстом и добавляется то, что регистр не знает: лестница уровней.
//
// Уровень НЕ хранится у клиента, а выводится из накопленного баланса — так он не
// может разъехаться с фактическими баллами. Уровень решает, сколько баллов можно
// списать одним документом.
//
// Почему проверки в событии, а не в транзакционном скрипте: и баланс регистра, и
// справочник уровней читаются асинхронно, а GetTransactions синхронный. Сервисом
// это не оформлено намеренно — скрипт своей же модели не видит контракт
// I<Сервис> этой модели (контракты собираются после её скриптов).
public partial class LoyaltyRedemptionEventHandler : TypedDocumentEventHandler<LoyaltyRedemption>
{

    public override async Task<EventResult> OnBeforePostAsync(LoyaltyRedemption header, EventContext context)
    {
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

        // Пустая лестница — уровни ещё не заведены; тогда лимитов нет и погашение
        // ограничено только балансом. Иначе клиент обязан достичь уровня.
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
