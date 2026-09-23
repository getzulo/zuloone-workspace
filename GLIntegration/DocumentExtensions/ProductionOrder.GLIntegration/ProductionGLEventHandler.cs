#nullable enable
using System;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Production cost already sits in ItemCostFifo (CostingIssue on consume,
// owner AfterPost on output). The books did not know: inventory stayed
// credited from the purchase forever, even while qty lived in WIP.
//
// One inventory account: RM and FG share it. WIP is the only extra
// balance-sheet bucket. Released: Dr WIP / Cr Inventory. Finished: the
// inverse, but only if the WIP journal exists. Draft→Finished is
// value-neutral on that one account — jumping would otherwise Dr Inventory
// without the matching credit.
//
// Mix does not unpost the Released journal (PostAsync is best-effort and
// keyed by description). Finish uses a different description so both
// journals may exist and net to zero.
[ExtensionOf("ProductionOrder")]
public partial class ProductionGLEventHandler : TypedDocumentEventHandler<ProductionOrder>
{
    public override async Task<EventResult> OnAfterPostAsync(ProductionOrder document, EventContext context)
    {
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Released" && document.Subtype != "Finished")
            return EventResult.Ok();

        var jeId = await PostToLedgerAsync(document, context);
        if (jeId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);

        var laborId = await PostLaborAsync(document, context);
        if (laborId.HasValue)
            await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, laborId.Value);

        return EventResult.Ok();
    }

    // ТРУД — ВТОРАЯ ПАРА, И БЕЗ НЕЁ КНИГА РАСХОДИТСЯ С РЕГИСТРОМ.
    //
    // Пара выше двусторонняя по одной сумме: Released Dr WIP / Cr запасы на
    // стоимость МАТЕРИАЛОВ, Finished — обратно. Она читает ItemCostFifo и
    // видит только отрицательные суммы, то есть списание компонентов.
    //
    // А слой выпуска, который заводит владелец документа, стоит материалы ПЛЮС
    // отнесённый труд. Оставить как было значило бы: регистр себестоимости
    // подорожал на труд, леджер — нет, и разница тихо живёт до сверки.
    // Поэтому на выпуске добавляется Dr запасы / Cr отнесённый труд.
    //
    // Счёт отнесённого труда — контрсчёт расхода на оплату труда: ФОТ начислен
    // отдельно (PayrollGLEventHandler), а эта проводка переносит его часть в
    // запас. Счёт не заполнен — проводки нет, как и у любой другой ноги
    // профиля: разноска best-effort и выпуск ронять не должна.
    private static async Task<Guid?> PostLaborAsync(ProductionOrder header, EventContext context)
    {
        if (header.Subtype != "Finished") return null;

        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;
        if (string.IsNullOrWhiteSpace(settings.LaborAbsorbedAccountCode)
            || string.IsNullOrWhiteSpace(settings.InventoryAccountCode))
            return null;

        var labor = await context.GetService<ILaborCostService>().OrderLaborCostAsync(header.MetaId);
        if (labor <= 0m) return null;

        var docs = context.GetService<IDocumentManager>();
        var order = await docs.GetDocumentAsync<ProductionOrder>(header.MetaId);
        if (order == null) return null;

        var le = await ResolveLegalEntityAsync(order.Location, context);
        if (le == null) return null;

        var date = order.DocumentDate == default ? DateTime.UtcNow.Date : order.DocumentDate.Date;
        return await gl.PostAsync(
            date, le.MetaId, le.Currency, labor,
            settings.InventoryAccountCode,
            settings.LaborAbsorbedAccountCode,
            "Production labor " + header.MetaId,
            "Труд в себестоимости выпуска",
            "Отнесение труда на изделие");
    }

    private static async Task<Guid?> PostToLedgerAsync(ProductionOrder header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;
        if (string.IsNullOrWhiteSpace(settings.WipAccountCode)
            || string.IsNullOrWhiteSpace(settings.InventoryAccountCode))
            return null;

        var docs = context.GetService<IDocumentManager>();
        var wipDesc = "WIP " + header.MetaId;
        var outputDesc = "Production output " + header.MetaId;

        if (header.Subtype == "Finished")
        {
            var wipJournals = await docs.CountDocumentsAsync<JournalEntry>(
                $"Description = '{wipDesc.Replace("'", "''")}'");
            if (wipJournals == 0) return null;
        }

        var cost = 0m;
        foreach (var row in await context.GetService<ITotalsManager>()
            .QueryMovementsAsync("ItemCostFifo", $"[DocumentMetaId] = '{header.MetaId}'"))
        {
            if (row.TryGetValue("Amount", out var amount) && amount != null)
            {
                var n = Convert.ToDecimal(amount);
                if (n < 0m) cost += -n;
            }
        }
        if (cost <= 0m) return null;

        var order = await docs.GetDocumentAsync<ProductionOrder>(header.MetaId);
        if (order == null) return null;

        var le = await ResolveLegalEntityAsync(order.Location, context);
        if (le == null) return null;

        var date = order.DocumentDate == default ? DateTime.UtcNow.Date : order.DocumentDate.Date;
        var isRelease = header.Subtype == "Released";
        return await gl.PostAsync(
            date, le.MetaId, le.Currency, cost,
            isRelease ? settings.WipAccountCode : settings.InventoryAccountCode,
            isRelease ? settings.InventoryAccountCode : settings.WipAccountCode,
            isRelease ? wipDesc : outputDesc,
            isRelease ? "Незавершёнка" : "Выпуск в запасы",
            isRelease ? "Списание в производство" : "Снятие незавершёнки");
    }

    private static async Task<LegalEntity?> ResolveLegalEntityAsync(Guid location, EventContext context)
    {
        var loc = await context.GetService<IDictionaryManager<StoreCell>>().GetRecordAsync(location);
        if (loc == null) return null;
        var zone = await context.GetService<IDictionaryManager<StoreZone>>().GetRecordAsync(loc.StoreZone);
        if (zone == null) return null;
        var wh = await context.GetService<IDictionaryManager<Store>>().GetRecordAsync(zone.Store);
        if (wh == null) return null;
        var div = await context.GetService<IDictionaryManager<Division>>().GetRecordAsync(wh.Division);
        if (div == null) return null;
        return await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(div.LegalEntity);
    }
}
