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
    ///   1. строка прайса ровно на эту единицу — цена задана как есть;
    ///   2. строка прайса на другую единицу того же товара — пересчёт;
    ///   3. умолчание товара (оно за базовую единицу) — пересчёт;
    ///   4. null.
    ///
    /// null — это «цена не задана», а НЕ ошибка: заполнение цен не обязано
    /// находить её для каждой строки.
    /// </summary>
    private async Task<decimal?> ResolveAsync(Guid item, Guid unit, Guid? priceList, DateTime onDate, bool sale)
    {
        if (priceList is Guid listId)
        {
            // По (прайс, товар) фильтруем в базе, а окно дат — в памяти:
            // сравнение дат строкой фильтра хрупко, а строк на один товар единицы.
            var rows = (await _rows.GetRecordsAsync($"PriceList = '{listId}' AND Item = '{item}'"))
                .Where(r => Covers(r, onDate))
                .ToList();

            var exact = rows.FirstOrDefault(r => r.Unit == unit);
            if (exact != null) return exact.Price;

            foreach (var row in rows)
            {
                var converted = await ConvertPriceAsync(item, row.Price, row.Unit, unit);
                if (converted != null) return converted;
            }
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
