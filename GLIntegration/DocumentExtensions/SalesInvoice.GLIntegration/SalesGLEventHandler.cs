#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Звено ЦЕПОЧКИ обработчиков SalesInvoice из модели GLIntegration: при выставлении
// счёта продажа разносится в главную книгу ДВУМЯ проводками — выручка
// (Dr дебиторка / Cr выручка) и себестоимость (Dr себестоимость / Cr запасы).
//
// Две проводки, а не одна на четыре строки: PostAsync разносит сбалансированную
// ПАРУ, и это не ограничение, а верное разделение — признание выручки и списание
// себестоимости живут по разным правилам (выручка есть всегда, себестоимости у
// товара без партий нет вовсе). Сумма себестоимости в ноль — второй проводки
// просто не будет, а счёт разнесётся как раньше.
//
// ВНИМАНИЕ: имя класса СВОЁ, не как у базового обработчика. Попытка назвать
// его именем базового (ради цепочки) не создаёт цепочку, а ВЫТЕСНЯЕТ родной
// обработчик Sales вместе с его проверкой остатка.
// Исходное рассуждение про имя класса:
// расширение чужого объекта оформляется звеном цепочки (конверт несёт
// extensionMetaId + baseClassName), и рантайм связывает звенья ПО ИМЕНИ КЛАССА.
// Со своим именем класса скрипт становится КОНКУРИРУЮЩИМ обработчиком того же
// документа — и не выполняется вовсе, потому что запускается только самый
// производный. Именно так эта разноска и молчала, когда у Sales появился
// собственный обработчик с проверкой остатка.
//
// Событие тонкое: сумма, юрлицо — и вызов GeneralLedgerService, где живёт вся
// механика проводки. Разноска best-effort: не настроены счета/период — тихо мимо.
public partial class SalesGLEventHandler : TypedDocumentEventHandler<SalesInvoice>
{
    public override async Task<EventResult> OnAfterPostAsync(SalesInvoice document, EventContext context)
    {
        if (document.Subtype != "Issued") return EventResult.Ok();

        try
        {
            var docs = context.GetService<IDocumentManager>();

            var jeId = await PostToLedgerAsync(document, context);
            if (jeId.HasValue)
                await docs.AddLinkAsync(document.MetaId, jeId.Value);

            var cogsId = await PostCostOfSalesAsync(document, context);
            if (cogsId.HasValue)
                await docs.AddLinkAsync(document.MetaId, cogsId.Value);
        }
        catch
        {
            // Разноска GL зависит от настройки и не должна ронять проведение счёта.
        }

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(SalesInvoice header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        // Строки заголовочного события пусты — документ перечитывается целиком.
        var inv = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesInvoice>(header.MetaId);
        if (inv == null) return null;

        // Сумма считается здесь, а не общим PricingService: в скомпилированном
        // обработчике событий его контракт приходит из другой версии сборки
        // ZuloOne.Services.Contracts и не кастится (в транзакционных скриптах —
        // работает). Округление то же, что у PricingService.
        var total = inv.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero));

        var le = await ResolveLegalEntityAsync(inv, context);
        if (le == null) return null;

        return await gl.PostAsync(
            inv.DocumentDate, le.MetaId, le.Currency, total,
            settings.ArAccountCode, settings.RevenueAccountCode,
            "Sales invoice " + header.MetaId,
            "Дебиторка по продаже", "Выручка от продажи");
    }

    /// <summary>
    /// Себестоимость проданного: Dr себестоимость / Cr запасы.
    ///
    /// Сумма НЕ пересчитывается по строкам счёта — она уже посчитана и записана.
    /// Списание себестоимости делает драйвер CostingIssue на регистре Stock
    /// («уменьшился остаток — списалась себестоимость»), и к моменту этого события
    /// его движения по ItemCostFifo уже лежат в базе с DocumentMetaId нашего счёта.
    /// Читается ФАКТ списания, а не своя копия расчёта: иначе метод оценки
    /// (FIFO/AVG из CostingSettings) пришлось бы повторять здесь, и учёт запаса
    /// разъехался бы с главной книгой ровно в тот день, когда настройку поменяют.
    ///
    /// Amount выбытия отрицателен (движок подставляет туда себестоимость слоёв
    /// вместо переданного нуля) — знак разворачивается, в проводку идёт модуль.
    /// Нет партий (товар заведён прямым движением регистра, а не приходом) —
    /// списывать нечего, сумма ноль, проводки нет.
    /// </summary>
    private async Task<Guid?> PostCostOfSalesAsync(SalesInvoice header, EventContext context)
    {
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        var moves = await context.GetService<ITotalsManager>()
            .QueryMovementsAsync("ItemCostFifo", $"[DocumentMetaId] = '{header.MetaId}'");

        var cost = 0m;
        foreach (var row in moves)
            if (row.TryGetValue("Amount", out var amount) && amount != null)
                cost -= Convert.ToDecimal(amount);
        if (cost <= 0m) return null;

        var inv = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesInvoice>(header.MetaId);
        if (inv == null) return null;

        var le = await ResolveLegalEntityAsync(inv, context);
        if (le == null) return null;

        return await gl.PostAsync(
            inv.DocumentDate, le.MetaId, le.Currency, cost,
            settings.CogsAccountCode, settings.InventoryAccountCode,
            "Cost of sales " + header.MetaId,
            "Себестоимость продажи", "Выбытие запасов");
    }

    /// <summary>Юрлицо продавца — с самого счёта: его фиксирует выставление
    /// (<c>SalesInvoiceEventHandler.OnBeforePost</c>) по цепочке Ячейка → Зона →
    /// Склад → Подразделение → Юрлицо. Читать поле, а не проходить цепочку заново,
    /// важно не ради экономии четырёх чтений: счёт мог быть выставлен от имени
    /// другого юрлица (агентская продажа со чужого склада), и проводка обязана
    /// попасть туда же, куда налог и сам счёт. Пусто — оргструктура не заполнена,
    /// разноска тихо пропускается.</summary>
    private static async Task<LegalEntity?> ResolveLegalEntityAsync(SalesInvoice invoice, EventContext context)
    {
        if (invoice.LegalEntity == Guid.Empty) return null;
        return await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(invoice.LegalEntity);
    }
}
