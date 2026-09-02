using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;

// Сервис "PricingService": контракт IPricingService. Две обязанности:
//
//   1. СУММА СТРОКИ — количество × цена, округлённое до денежной точности.
//      Раньше `line.Quantity * line.UnitPrice` копипастилось по проводкам Sales,
//      Purchasing, CRM, Localization, Costing и в событии GL — теперь одна формула.
//   2. РАЗРЕШЕНИЕ ЦЕНЫ — какая цена у товара в этой единице на эту дату.
//      До этого такой вопрос в системе не задавал никто: цену вводили руками.
//
// Точность берётся из глобальной константы AmountScale (та же настройка, что у
// MeasurementService).
public partial class PricingService
{
    // Defense-in-depth рядом с проверкой цикла на сохранении (PriceListEventHandler) —
    // та уже не даёт сохранить зацикленную цепочку, здесь только страховка. Одна и
    // та же константа GlobalConstants.Pricing.PriceTypeChainMaxDepth — там же.
    private static int MaxPriceTypeChainDepth => GlobalConstants.Get<int?>("PriceTypeChainMaxDepth") ?? 20;

    private readonly IDictionaryManager<PriceList> _lists;
    private readonly IDictionaryManager<PriceListItem> _rows;
    private readonly IDictionaryManager<Item> _items;

    public PricingService(
        IDictionaryManager<PriceList> lists,
        IDictionaryManager<PriceListItem> rows,
        IDictionaryManager<Item> items)
    {
        _lists = lists;
        _rows = rows;
        _items = items;
    }

    /// <summary>Сумма строки = количество × цена, округлённая до денежной точности.</summary>
    public decimal LineAmount(decimal quantity, decimal unitPrice)
        => LineAmount(quantity, unitPrice, 0m);

    /// <summary>
    /// Сумма строки со скидкой. Скидка — ПРОЦЕНТ (15 = 15%), а не доля: так она
    /// задана в LoyaltyTier.DiscountPercent, и путать её с налоговой ставкой,
    /// которая хранится долей (0.15), нельзя.
    ///
    /// Скидка приходит параметром, а не читается из CRM: Inventory лежит ниже
    /// CRM по слоям и о существовании уровней лояльности не знает. Кто скидку
    /// нашёл, тот её и передаёт — и все денежные ноги документа обязаны
    /// передать ОДНУ И ТУ ЖЕ, иначе выручка, НДС и баллы разъедутся.
    /// </summary>
    public decimal LineAmount(decimal quantity, decimal unitPrice, decimal discountPercent)
    {
        var gross = quantity * unitPrice;
        var net = discountPercent > 0m ? gross * (1m - discountPercent / 100m) : gross;
        return Math.Round(net, GlobalConstants.Get<int?>("AmountScale") ?? 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Цена ПРОДАЖИ товара за указанную единицу на дату; null — цены нет.
    /// Прайс-лист берётся у клиента (Customer.PriceList); клиент без прайса —
    /// не ошибка, сработает умолчание товара.
    /// </summary>
    public async Task<decimal?> ResolveSalePriceAsync(Guid item, Guid unit, Guid? customer, DateTime onDate)
        => await ResolveAsync(item, unit, await PriceListOfAsync(customer, sale: true), onDate, sale: true);

    /// <summary>Цена ЗАКУПКИ товара за указанную единицу на дату; null — цены нет.</summary>
    public async Task<decimal?> ResolvePurchasePriceAsync(Guid item, Guid unit, Guid? supplier, DateTime onDate)
        => await ResolveAsync(item, unit, await PriceListOfAsync(supplier, sale: false), onDate, sale: false);

    /// <summary>
    /// Захватывает фактическую цену строки продажи в историю (PriceListItem) типа
    /// цен клиента, если тот Base. Вызывается при проведении счёта — см.
    /// <see cref="CapturePriceAsync"/>.
    /// </summary>
    public Task CaptureSalePriceAsync(Guid item, Guid unit, Guid? customer, decimal price, DateTime onDate)
        => CapturePriceAsync(item, unit, customer, sale: true, price, onDate);

    /// <summary>Зеркало <see cref="CaptureSalePriceAsync"/> для закупки.</summary>
    public Task CapturePurchasePriceAsync(Guid item, Guid unit, Guid? supplier, decimal price, DateTime onDate)
        => CapturePriceAsync(item, unit, supplier, sale: false, price, onDate);

    /// <summary>
    /// Строит настоящую историю цен из проведённых документов: закрывает
    /// действовавшую строку днём раньше и открывает новую с EffectiveFrom = onDate.
    /// Работает только для типа цен Kind == Base — Calculated строк не хранит.
    ///
    /// Прошлое НИКОГДА не переписывается: попадание в уже закрытую (EffectiveTo
    /// заполнен) строку с другой ценой молча пропускается, без лога — тот же
    /// best-effort, что и у SpawnPutAwayTaskAsync в Purchasing. Повторный захват в
    /// тот же день правит строку на месте (идемпотентность при перепроведении),
    /// а не плодит нулевые окна.
    /// </summary>
    private async Task CapturePriceAsync(Guid item, Guid unit, Guid? party, bool sale, decimal price, DateTime onDate)
    {
        // Unit на строке документа необязателен — пустая единица или
        // нулевая/отрицательная цена значит «в этой строке писать нечего».
        if (item == Guid.Empty || unit == Guid.Empty || price <= 0m) return;

        var priceTypeId = await PriceListOfAsync(party, sale);
        if (priceTypeId is not Guid listId) return;

        var priceType = await _lists.GetRecordAsync(listId);
        // PriceListOfAsync проверяет IsDisabled/Direction, но не Kind — Calculated
        // строк не хранит вовсе (PriceListItemEventHandler отклонит запись под ним).
        if (priceType == null || priceType.Kind != PriceListKind.Base) return;

        var rows = await _rows.GetRecordsAsync($"PriceList = '{listId}' AND Item = '{item}' AND Unit = '{unit}'");
        var onDateOnly = onDate.Date;
        var covering = rows.FirstOrDefault(r => Covers(r, onDateOnly));

        if (covering != null)
        {
            if (covering.Price == price) return; // уже эта цена — нечего писать

            if (covering.EffectiveTo != null) return; // запечатанная история — прошлое не трогаем

            if ((covering.EffectiveFrom ?? DateTime.MinValue).Date == onDateOnly)
            {
                // тот же день (перепроведение или второй документ в тот же день) —
                // правим строку на месте, а не плодим нулевые окна
                covering.Price = price;
                await _rows.SaveRecordAsync(covering);
                return;
            }

            covering.EffectiveTo = onDateOnly.AddDays(-1);
            await _rows.SaveRecordAsync(covering);
        }

        // Сиблинги гарантированно непересекающиеся (инвариант проверяется на
        // сохранении в PriceListItemEventHandler) — "next" здесь единственный
        // кандидат, столкновения с ним не будет никогда.
        var next = rows.Where(r => r.EffectiveFrom > onDateOnly).OrderBy(r => r.EffectiveFrom).FirstOrDefault();
        await _rows.SaveRecordAsync(new PriceListItem
        {
            PriceList = listId,
            Item = item,
            Unit = unit,
            Price = price,
            EffectiveFrom = onDateOnly,
            EffectiveTo = next?.EffectiveFrom?.Date.AddDays(-1),
        });
    }

    /// <summary>
    /// Прайс-лист контрагента, если он назначен, действует и заведён на нужную
    /// сторону сделки. Прайс продажи не имеет права подставиться в закупку —
    /// поэтому направление проверяется здесь, а не полагается на дисциплину.
    /// </summary>
    private async Task<Guid?> PriceListOfAsync(Guid? party, bool sale)
    {
        if (party == null || party == Guid.Empty) return null;

        Guid listId;
        if (sale)
        {
            var customer = await ScriptServices.Get<IDictionaryManager<Customer>>().GetRecordAsync(party.Value);
            listId = customer?.PriceList ?? Guid.Empty;
        }
        else
        {
            var supplier = await ScriptServices.Get<IDictionaryManager<Supplier>>().GetRecordAsync(party.Value);
            listId = supplier?.PriceList ?? Guid.Empty;
        }
        if (listId == Guid.Empty) return null;

        var list = await _lists.GetRecordAsync(listId);
        if (list == null || list.IsDisabled) return null;
        var wanted = sale ? PriceDirection.Sale : PriceDirection.Purchase;
        return list.Direction == wanted ? listId : (Guid?)null;
    }

    /// <summary>
    /// Лестница поиска цены. Порядок повторяет ItemQuantityConverter — от самого
    /// точного к самому общему, с остановкой на первом ответе:
    ///
    ///   1. тип цены (с учётом Kind — см. ResolvePriceForTypeAsync);
    ///   2. умолчание товара (оно за базовую единицу) — пересчёт;
    ///   3. null.
    ///
    /// null — это «цена не задана», а НЕ ошибка: заполнение цен не обязано
    /// находить её для каждой строки.
    /// </summary>
    private async Task<decimal?> ResolveAsync(Guid item, Guid unit, Guid? priceList, DateTime onDate, bool sale)
    {
        if (priceList is Guid listId)
        {
            var resolved = await ResolvePriceForTypeAsync(item, unit, listId, onDate, depth: 0);
            if (resolved != null) return resolved;
        }

        var card = await _items.GetRecordAsync(item);
        if (card == null) return null;

        // Умолчание товара задано за его базовую единицу. Ноль здесь означает
        // «не заполнено»: поле необязательное, а необязательный Decimal генерится
        // ненулевым.
        var fallback = sale ? card.DefaultSalePrice : card.DefaultPurchasePrice;
        if (fallback <= 0m) return null;

        return unit == card.UnitOfMeasure
            ? fallback
            : await ConvertPriceAsync(item, fallback, card.UnitOfMeasure, unit);
    }

    /// <summary>
    /// Цена по конкретному ТИПУ ЦЕНЫ. Base — строка PriceListItem ровно на эту
    /// единицу, иначе на другую единицу того же товара с пересчётом. Calculated —
    /// рекурсивно цена базового типа (BasePriceType) с применённой наценкой
    /// (MarkupPercent, может быть отрицательной). Умолчание товара сюда
    /// НЕ включено: оно крайняя ступень лестницы в ResolveAsync, а не часть
    /// разрешения типа — иначе два разных Calculated-типа без собственной цены в
    /// базовом тихо сошлись бы к одному умолчанию товара мимо своих наценок.
    /// </summary>
    private async Task<decimal?> ResolvePriceForTypeAsync(Guid item, Guid unit, Guid priceTypeId, DateTime onDate, int depth)
    {
        if (depth > MaxPriceTypeChainDepth) return null;

        var priceType = await _lists.GetRecordAsync(priceTypeId);
        if (priceType == null || priceType.IsDisabled) return null;

        if (priceType.Kind == PriceListKind.Base)
        {
            // По (тип цены, товар) фильтруем в базе, а окно дат — в памяти:
            // сравнение дат строкой фильтра хрупко, а строк на один товар единицы.
            var rows = (await _rows.GetRecordsAsync($"PriceList = '{priceTypeId}' AND Item = '{item}'"))
                .Where(r => Covers(r, onDate))
                .ToList();

            var exact = rows.FirstOrDefault(r => r.Unit == unit);
            if (exact != null) return exact.Price;

            // Несколько строк на РАЗНЫЕ единицы этого товара — обычная настройка
            // (ящик и паллета оценены отдельно, не пропорционально). Если все, что
            // удалось перевести в запрошенную единицу, сходятся в одну цену —
            // отдаём её; если расходятся, порядок строк из базы ничем не
            // гарантирован, и угадывать одну из них молча нельзя — честный ответ
            // здесь «цена неоднозначна», то есть null, как и при отсутствии цены.
            decimal? found = null;
            foreach (var row in rows)
            {
                var converted = await ConvertPriceAsync(item, row.Price, row.Unit, unit);
                if (converted == null) continue;
                if (found != null && found.Value != converted.Value) return null;
                found ??= converted;
            }

            return found;
        }

        // Kind == Calculated: цена базового типа, к ней применяется наценка.
        // Direction базового типа намеренно не проверяется — дилерская цена
        // продажи вправе считаться от закупочной, это ключевой сценарий фичи.
        if (priceType.BasePriceType == Guid.Empty) return null;

        var basePrice = await ResolvePriceForTypeAsync(item, unit, priceType.BasePriceType, onDate, depth + 1);
        if (basePrice == null) return null;

        var marked = basePrice.Value * (1m + priceType.MarkupPercent / 100m);
        var rounded = Math.Round(marked, GlobalConstants.Get<int?>("AmountScale") ?? 2, MidpointRounding.AwayFromZero);

        // Наценка вплотную к -100% (разрешена — floor на сохранении режет только
        // <= -100%) на уже маленькой базовой цене может обнулиться ИМЕННО
        // округлением этой ступени, а не по вине конкретных чисел где-то ещё.
        // Отдать 0 как «цену» — продать по нулю молча; честный ответ здесь тот
        // же, что и при отсутствии цены вовсе, — пусть лестница уйдёт на
        // следующую ступень (умолчание товара), а не остановится на нуле.
        return rounded > 0m ? rounded : (decimal?)null;
    }

    /// <summary>Действует ли строка прайса на дату. Пустая граница — открытый конец.</summary>
    private static bool Covers(PriceListItem row, DateTime onDate)
        => onDate >= (row.EffectiveFrom ?? DateTime.MinValue)
        && onDate <= (row.EffectiveTo ?? DateTime.MaxValue);

    /// <summary>
    /// Перевод ЦЕНЫ между единицами — обратный переводу количества: если в ящике
    /// 12 штук, то ящиков меньше, а цена за ящик БОЛЬШЕ. Поэтому цена умножается
    /// на то отношение, на которое количество делится.
    ///
    /// Отношение берётся ТОВАРНЫМ конвертером, а не общим: «ящик» сам по себе
    /// коэффициента не имеет, он зависит от товара. Заодно бесплатно работают и
    /// упаковки, и виды величины, и тождество — лестница у конвертера своя и
    /// уже отлажена.
    /// </summary>
    private static async Task<decimal?> ConvertPriceAsync(Guid item, decimal price, Guid fromUnit, Guid toUnit)
    {
        if (fromUnit == toUnit) return price;

        var conversion = ScriptServices.Get<IItemQuantityConverter>();
        var oneTarget = await conversion.ToBaseAsync(item, 1m, toUnit);
        var oneSource = await conversion.ToBaseAsync(item, 1m, fromUnit);
        // Перевести нечем — молча считать цену за штуку ценой за ящик нельзя.
        if (oneTarget == null || oneSource == null || oneSource.Value == 0m) return null;

        var amount = price * oneTarget.Value / oneSource.Value;
        return Math.Round(amount, GlobalConstants.Get<int?>("AmountScale") ?? 2, MidpointRounding.AwayFromZero);
    }
}
