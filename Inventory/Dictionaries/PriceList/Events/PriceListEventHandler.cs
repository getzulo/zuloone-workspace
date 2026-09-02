#nullable enable
using System.Linq;
using ZuloOne.Core.Services;

namespace ZuloOne.Runtime.Generated;

// Проверки типа цены. Строки с ценами проверяет PriceListItemEventHandler —
// здесь только сам заголовок.
public partial class PriceListEventHandler : TypedDictionaryEventHandler<PriceList>
{
    // Одна и та же константа GlobalConstants.Pricing.PriceTypeChainMaxDepth —
    // используется и здесь, и в PricingService.MaxPriceTypeChainDepth.
    private static int MaxPriceTypeChainDepth => GlobalConstants.Get<int?>("PriceTypeChainMaxDepth") ?? 20;

    public override async Task<EventResult> OnBeforeSaveAsync(PriceList record, bool isNew, EventContext context)
    {
        // Имя — то, чем тип цены выбирают в карточке клиента, поэтому оно
        // должно быть различимым. Уникальность проверяется здесь, а не
        // индексом: имя редактируемое, и внятный отказ полезнее ошибки СУБД.
        var manager = context.GetService<IDictionaryManager<PriceList>>();
        var duplicate = (await manager
                .GetRecordsAsync($"Name = '{record.Name?.Replace("'", "''")}'"))
            .FirstOrDefault(r => r.MetaId != record.MetaId);
        if (duplicate != null)
            return EventResult.Cancel("Тип цены с таким наименованием уже есть");

        if (record.Kind == PriceListKind.Base)
        {
            if (record.BasePriceType != Guid.Empty || record.MarkupPercent != 0)
                return EventResult.Cancel(
                    "Базовый тип цены не может ссылаться на другой тип цены и не может иметь наценку — заполни цены строками (Price type rows)");
            return EventResult.Ok();
        }

        // Kind == Calculated
        if (record.BasePriceType == Guid.Empty)
            return EventResult.Cancel("Динамический тип цены обязан ссылаться на базовый тип цены (Base price type)");

        // -100% и меньше даёт множитель (1 + MarkupPercent/100) <= 0 — цена базового
        // типа обнулилась бы или ушла в минус независимо от того, какой она окажется.
        // Проверка статическая и не зависит от цепочки: множитель > -100 остаётся
        // положительным на любом числе шагов, поэтому одной ступени достаточно.
        if (record.MarkupPercent <= -100m)
            return EventResult.Cancel("Наценка не может быть -100% или меньше — цена базового типа обнулится или уйдёт в минус");

        // Симметрично PriceListItemEventHandler: у Calculated не должно быть
        // строк цены. Актуально при переключении уже существующего Base-типа,
        // под которым строки успели завестись, — иначе они молча становятся
        // мёртвыми данными, которые лестница подбора больше не читает. Для
        // ещё не сохранённой записи record.MetaId — одноразовый Guid (см. ниже),
        // под ним заведомо не может быть ни одной строки, ложных срабатываний нет.
        var existingRows = await context.GetService<IDictionaryManager<PriceListItem>>()
            .GetRecordsAsync($"PriceList = '{record.MetaId}'");
        if (existingRows.Any())
            return EventResult.Cancel(
                "У этого типа цены уже есть строки (Price type rows) — удали их перед переключением в Calculated, "
                + "иначе они станут мёртвыми данными, которые лестница подбора цены не читает");

        // На isNew record.MetaId здесь — одноразовый Guid материализации хука,
        // не тот, что попадёт в БД (см. zuloone-new-dictionary §2б), поэтому
        // само-ссылку это не ловит на первой вставке — но и не должно: сослаться
        // на ещё не существующую запись неоткуда, UI-пикер не выберет то, чего
        // ещё нет. Само-ссылка достижима только обновлением уже сохранённой
        // записи, а там MetaId уже реальный — тот же обход корректно ловит и её,
        // и транзитивный цикл.
        var visited = new HashSet<Guid> { record.MetaId };
        var currentId = record.BasePriceType;
        var depth = 0;
        while (true)
        {
            if (!visited.Add(currentId))
                return EventResult.Cancel("Цепочка базовых типов цены зациклилась");

            if (++depth > MaxPriceTypeChainDepth)
                return EventResult.Cancel($"Цепочка базовых типов цены длиннее {MaxPriceTypeChainDepth} уровней");

            var current = await manager.GetRecordAsync(currentId);
            if (current == null || current.Kind == PriceListKind.Base || current.BasePriceType == Guid.Empty)
                break;

            currentId = current.BasePriceType;
        }

        return EventResult.Ok();
    }
}
