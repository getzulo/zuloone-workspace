#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Purchase order validation: a receipt must have lines and every line a positive
// quantity. Lines are re-loaded via IDocumentManager (the header event does not
// carry table parts).
public partial class PurchaseOrderEventHandler : TypedDocumentEventHandler<PurchaseOrder>
{
    public override async Task<EventResult> OnBeforePostAsync(PurchaseOrder document, EventContext context)
    {
        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<PurchaseOrder>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;

        if (lines.Count == 0)
            return EventResult.Cancel("Заказ без строк не проводится");

        foreach (var line in lines)
        {
            if (line.Quantity <= 0m)
                return EventResult.Cancel("Количество в строке должно быть больше нуля");
        }

        // Налоговый контур НАСТРОЕН, но на дату прихода действующей ставки нет —
        // приход не проводится. Зеркало проверки у счёта продажи, и стоит она в
        // ОТМЕНЯЕМОМ событии по той же причине: в OnAfterPost, где порождается сам
        // расчёт, платформа превращает отказ обработчика в предупреждение в логе:
        // документ проводится, а возмещаемый входной налог пропадает молча.
        if (document.Subtype == "Received")
        {
            // Адресная дисциплина: принимать положено в ячейку ПРИЁМКИ, а дальше
            // товар переносит задание раскладки. Проверка спрашивает Inventory, а
            // не сравнивает имя типа ячейки: набор ролей лежит в метаданных.
            // Дисциплина выключена (умолчание) — сервис отвечает «годится любая»,
            // и приход ведёт себя как раньше.
            var cells = context.GetService<IStoreCellService>();
            if (!await cells.IsCellAllowedForAsync(document.Location, StoreCellPurpose.Receiving))
                return EventResult.Cancel(
                    "Приход оформляется в ячейку ПРИЁМКИ — у выбранной ячейки другое назначение");

            var tax = context.GetService<ITaxService>();
            var taxCode = await tax.ResolveDefaultTaxCodeAsync();
            if (taxCode is not null && await tax.ResolveRateAsync(taxCode.Value, TaxPointOf(document)) is null)
                return EventResult.Cancel(
                    $"Налоговый код настроен, но действующей ставки на {TaxPointOf(document):yyyy-MM-dd} нет — приход не проводится");
        }

        return EventResult.Ok();
    }

    /// <summary>Дата налогового события — дата документа; незаполненная датируется
    /// сегодняшним днём ровно так же, как её проставляет IDocumentManager при создании.</summary>
    private static DateTime TaxPointOf(PurchaseOrder document)
        => document.DocumentDate == default ? DateTime.UtcNow.Date : document.DocumentDate.Date;

    // Оприходование порождает расчёт ВХОДНОГО налога — зеркало выходного у
    // счёта продажи. Тот же сервис и та же необязательность контура: разница
    // ровно в коде направления, поэтому вход и выход не могут разъехаться.
    // Входной налог возмещаемый, поэтому он обязан попасть в тот же леджер, что
    // и выходной, — иначе декларация посчитает налог к уплате с полной выручки.
    public override async Task<EventResult> OnAfterPostAsync(PurchaseOrder document, EventContext context)
    {
        if (document.Subtype != "Received") return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var order = await docs.GetDocumentAsync<PurchaseOrder>(document.MetaId);
        if (order is null || order.Lines.Count == 0) return EventResult.Ok();

        var pricing = context.GetService<IPricingService>();
        var legalEntity = await context.GetService<IStoreCellService>().GetLegalEntityAsync(order.Location);
        if (legalEntity is not null)
        {
            var taxBase = order.Lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice));

            // Ставка подбирается на ДАТУ ПРИХОДА, не на сегодня: иначе документ и
            // его налог датировались бы по-разному, а оприходование задним числом
            // посчиталось бы по сегодняшней ставке.
            var calc = await context.GetService<ITaxService>()
                .CreateCalculationAsync(legalEntity.Value, "INPUT", taxBase, $"Purchase order {document.Number}", TaxPointOf(document));
            if (calc.HasValue)
                await docs.AddLinkAsync(document.MetaId, calc.Value);

            await SpawnPutAwayTaskAsync(order, context);
        }

        // Захват цены в историю — самостоятельная забота: срабатывает даже если
        // юрлицо не резолвится. Одна и та же (Item,Unit) на двух строках —
        // выигрывает последняя (порядок вызовов — порядок строк документа).
        foreach (var line in order.Lines)
            await pricing.CapturePurchasePriceAsync(line.Item, line.Unit, order.Supplier, line.UnitPrice, TaxPointOf(document));

        return EventResult.Ok();
    }

    /// <summary>
    /// Принятый товар лежит в ячейке приёмки и должен уехать на хранение — это
    /// работа кладовщика, а не бухгалтера, поэтому приход сам заводит ей ЧЕРНОВИК
    /// задания раскладки. Черновик, а не проведённое: физически товар ещё не
    /// переставили, подтверждает человек.
    ///
    /// ИДЕМПОТЕНТНОСТЬ ОБЯЗАТЕЛЬНА. Событие after-post исполняется заново при
    /// КАЖДОМ проведении, а приход перепроводят буднично — правка накладной.
    /// Без проверки второе проведение заводило бы второе задание на тот же товар
    /// (проверено: со снятой проверкой тест видит два). Отдельно замечу, что
    /// удвоение, которым страдают ПРОДАЖИ (драйвер себестоимости дописывает
    /// движения и заставляет событие сработать дважды за одно проведение), здесь
    /// ни при чём: приход увеличивает склад, драйвер срабатывает на чистом
    /// минусе, вторичных движений нет.
    ///
    /// Ключ — ГРАФ ДОКУМЕНТОВ, а не колонка с id: указатель документа на документ
    /// в этой платформе выражается связью.
    ///
    /// Best-effort, как разноска в GL: нет ячейки хранения — задания нет, приход
    /// проводится. Иначе незаполненная настройка склада роняла бы закупку.
    /// </summary>
    private static async Task SpawnPutAwayTaskAsync(PurchaseOrder order, EventContext context)
    {
        var cells = context.GetService<IStoreCellService>();
        if (!await cells.IsWarehouseDisciplineOnAsync()) return;

        var docs = context.GetService<IDocumentManager>();

        // Ребро несёт только id концов, тип — у узла: сопоставляем одно с другим.
        // Ищется именно РЕБРО от этого заказа, а не любой родственник типа
        // «раскладка» в графе: семья обходит связи в обе стороны на восемь шагов,
        // и чужое задание, попавшее в неё окольным путём, отменило бы создание
        // своего.
        var family = await docs.GetDocumentFamilyAsync(order.MetaId);
        var putAwayIds = new HashSet<Guid>(
            family.Nodes.Where(n => n.DocTypeMetaId == PutAwayTaskType).Select(n => n.DocId));
        if (family.Edges.Any(e => e.ParentDocId == order.MetaId && putAwayIds.Contains(e.ChildDocId))) return;

        var store = await cells.GetStoreAsync(order.Location);
        if (store is null) return;
        var storageCell = await cells.SuggestStorageCellAsync(store.Value);
        if (storageCell is null) return;

        var task = await docs.NewDocumentAsync<PutAwayTask>("Draft", new Dictionary<string, object?>
        {
            ["FromCell"] = order.Location,
        });
        foreach (var line in order.Lines)
            task.Lines.Add(new PutAwayTaskLinesTablePartRow
            {
                Item = line.Item,
                Quantity = line.Quantity,
                Unit = line.Unit,
                ToCell = storageCell.Value,
            });

        await docs.SaveDocumentAsync(task);
        await docs.AddLinkAsync(order.MetaId, task.MetaId);
    }

    /// <summary>Тип документа «раскладка» — по нему ищется уже созданное задание.</summary>
    private static readonly Guid PutAwayTaskType = Guid.Parse("57100701-0000-4000-8000-000000000000");
}
