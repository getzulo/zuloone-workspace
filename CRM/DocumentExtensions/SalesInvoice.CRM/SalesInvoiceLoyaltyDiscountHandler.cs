#nullable enable
using System.Linq;

namespace ZuloOne.Runtime.Generated;

// Расширение счёта продажи МОДЕЛЬЮ CRM: при выставлении в шапку счёта
// проставляется скидка уровня лояльности клиента.
//
// ГДЕ ИМЕННО ЭТО ПРОИСХОДИТ. Не на проведении, а на записи ПОДТИПА. Подтип
// меняет отдельный движок: SaveDocumentAsync его вообще не пишет (Subtype в
// списке полей, которыми владеет платформа), а SetSubtypeAsync делает
// точечный UPDATE одной колонки — и вот его-то before-событие мы и ловим.
// Значение попадает в тот же UPDATE (WriteBack), то есть оказывается в базе
// ДО того, как движок соберёт проводки.
//
// Поставить скидку в OnBeforePost нельзя: экземпляр события в базу не пишется
// (WriteBack на пост-событиях нет), а точечная запись оттуда до проводок уже не
// доезжает. Скидка применилась бы только к налоговой базе в OnAfterPost, которая
// перечитывает документ, — и дебиторка, выручка, НДС и баллы посчитались бы без
// неё. Ровно то расхождение леджера, ради которого скидка и вынесена в шапку.
//
// ДОКУМЕНТ ПЕРЕЧИТЫВАЕТСЯ. В событие частичного обновления приходят ТОЛЬКО
// пишущиеся колонки — здесь это один Subtype, а Customer и текущая скидка в нём
// нули. Судить по такому экземпляру о документе нельзя.
//
// Уровень выводится из НАКОПЛЕННОГО баланса баллов, а не хранится у клиента: так
// он не может разъехаться с фактическими баллами. Баланс берётся до начисления
// баллов этим же счётом — счёт ещё не проведён.
public partial class SalesInvoiceLoyaltyDiscountHandler : TypedDocumentEventHandler<SalesInvoice>
{
    public override async Task<EventResult> OnBeforeSaveAsync(SalesInvoice header, bool isNew, EventContext context)
    {
        if (isNew || header.Subtype != "Issued" || header.MetaId == Guid.Empty) return EventResult.Ok();

        var stored = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesInvoice>(header.MetaId);
        if (stored == null || stored.Customer == Guid.Empty) return EventResult.Ok();

        // Скидка, введённая руками, — решение человека (договорённость по сделке).
        // Уровень лояльности её не переписывает, как и юрлицо продавца.
        if (stored.DiscountPercent > 0m) return EventResult.Ok();

        var balance = await context.GetService<ITotalsManager>().GetBalanceAsync("LoyaltyPoints",
            new Dictionary<string, object?> { ["Customer"] = stored.Customer });
        var points = balance != null && balance.TryGetValue("Points", out var raw) && raw != null
            ? Convert.ToDecimal(raw)
            : 0m;

        var reached = (await context.GetService<IDictionaryManager<LoyaltyTier>>()
                .GetRecordsAsync($"MinPoints <= {points}"))
            .OrderByDescending(t => t.MinPoints)
            .FirstOrDefault();

        // Уровня нет (лестница пуста или баллов не хватает) — скидки нет, и это
        // не ошибка: счёт выставляется по полной цене.
        if (reached == null || reached.DiscountPercent <= 0m) return EventResult.Ok();

        header.DiscountPercent = reached.DiscountPercent;
        return EventResult.Ok();
    }
}
