using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Generated;
// ЯВНЫЙ using, хотя ZuloOne.Runtime.Services уже в global usings скриптов:
// сгенерированный исходник контракта копирует только НЕ-глобальные using'и файла,
// а сборка контрактов компилируется без script framework. Без этой строки
// IQuantityConverter/QuantityConversionRequest в сигнатуре не разрешатся (CS0246),
// и упадёт НЕ один сервис, а ВЕСЬ реестр.
using ZuloOne.Runtime.Services;

// Пересчёт количества строки документа в базовую единицу ТОВАРА.
//
// Порядок разбора — от частного к общему, и он существенен:
//   1. ТОЖДЕСТВО. Единица строки совпадает с базовой единицей товара — вернуть
//      количество. Платформа намеренно НЕ делает здесь короткого замыкания, и
//      null означал бы отказ в записи каждой строки, введённой в базовой
//      единице, то есть подавляющего большинства строк.
//   2. УПАКОВКА ТОВАРА (ItemUnit). «Коробка» без товара не величина: у одного
//      товара в ней 12 штук, у другого 6. Поэтому упаковка спрашивается ПЕРВОЙ
//      и только для того товара, который стоит в строке.
//   3. ВИД ВЕЛИЧИНЫ (UnitClass + RatioToBase). Килограммы в граммы одинаковы для
//      любого товара, здесь товар не нужен.
//   4. Иначе null — «правила нет». ИСКЛЮЧЕНИЙ ОТСЮДА НЕ БРОСАЕМ: отказать в
//      записи решает платформа, конвертер это решение не принимает.
//
// Живёт в Inventory, а не в Common, потому что упаковка ссылается на Item, и
// слой 1 на него ссылаться не может. Это цена товарных упаковок: класс-перевод
// доступен всем моделям (UnitConverter в Common), товарный — только со слоя 2.
//
// ВСЕ чтения — через request.Reader. Своё соединение открывать нельзя: внешняя
// транзакция промоутнется в MSDTC (на Linux его нет), а suppress-скоуп на SQL
// Server заблокируется на строках, залоченных текущей транзакцией, — тест,
// посеявший упаковку внутри своего отката, повис бы вместо падения.
public partial class ItemQuantityConverter : IQuantityConverter
{
    private readonly IDictionaryManager<ItemUnit> _packs;
    private readonly IDictionaryManager<UnitOfMeasure> _units;
    private readonly IDictionaryManager<Item> _items;

    public ItemQuantityConverter(
        IDictionaryManager<ItemUnit> packs,
        IDictionaryManager<UnitOfMeasure> units,
        IDictionaryManager<Item> items)
    {
        _packs = packs;
        _units = units;
        _items = items;
    }

    // ───────────────── прикладной вход ─────────────────

    /// <summary>
    /// Количество товара в его базовой единице; null — перевести нечем.
    /// Сначала упаковка этого товара, затем общий вид величины.
    /// </summary>
    public async Task<decimal?> ToBaseAsync(Guid item, decimal quantity, Guid fromUnit)
    {
        var itemRecord = await _items.GetRecordAsync(item);
        if (itemRecord == null) return null;
        var baseUnit = itemRecord.UnitOfMeasure;

        if (fromUnit == baseUnit) return quantity;

        var pack = (await _packs.GetRecordsAsync($"Item = '{item}' AND Unit = '{fromUnit}'")).FirstOrDefault();
        if (pack != null && pack.QtyInBaseUnit > 0m) return quantity * pack.QtyInBaseUnit;

        var from = await _units.GetRecordAsync(fromUnit);
        var to = await _units.GetRecordAsync(baseUnit);
        if (from == null || to == null) return null;

        return ByRatio(
            quantity, from.UnitClass, from.RatioToBase, to.UnitClass, to.RatioToBase);
    }

    /// <summary>То же с округлением до точности базовой единицы товара.</summary>
    public async Task<decimal?> ToBaseRoundedAsync(Guid item, decimal quantity, Guid fromUnit)
    {
        var converted = await ToBaseAsync(item, quantity, fromUnit);
        if (!converted.HasValue) return null;

        var itemRecord = await _items.GetRecordAsync(item);
        var baseUnit = itemRecord == null ? Guid.Empty : itemRecord.UnitOfMeasure;
        var unit = baseUnit == Guid.Empty ? null : await _units.GetRecordAsync(baseUnit);
        return RoundQty(converted.Value, unit?.DecimalPlaces ?? FallbackScale());
    }

    // ───────────────── платформенный вход: IQuantityConverter ─────────────────

    public async Task<decimal?> ConvertAsync(QuantityConversionRequest request, CancellationToken ct = default)
    {
        decimal? converted;

        if (request.FromUnit == request.ToUnit)
        {
            converted = request.Quantity;
        }
        else
        {
            // Имя поля-ссылки на товар заранее неизвестно (в строке заказа это
            // Item, в составе спецификации — Component), и в запросе его нет.
            // Идём с другой стороны: берём упаковки этой ЕДИНИЦЫ и проверяем,
            // встречается ли их товар среди значений строки. Совпадение
            // однозначно — упаковка привязана к паре (товар, единица).
            var packQty = await PackFactorAsync(request.Reader, request.Row, request.FromUnit, ct);
            converted = packQty.HasValue
                ? request.Quantity * packQty.Value
                : await ByClassAsync(request.Reader, request.Quantity, request.FromUnit, request.ToUnit, ct);
        }

        if (!converted.HasValue) return null;

        // Точность единицы, ограниченная масштабом колонки-приёмника: единица с
        // шестью знаками, ложась в DECIMAL(18,4), была бы молча обрезана драйвером.
        var scale = Math.Min(
            await UnitPrecisionAsync(request.Reader, request.ToUnit, ct), request.TargetScale);
        return RoundQty(converted.Value, scale);
    }

    /// <summary>Сколько базовых единиц товара в одной упаковке; null — упаковки нет.</summary>
    private static async Task<decimal?> PackFactorAsync(
        IRowReader reader, IReadOnlyDictionary<string, object?> row, Guid unit, CancellationToken ct)
    {
        var packs = await reader.ReadAsync("ItemUnit", $"Unit = '{unit:D}'", ct);
        if (packs.Count == 0) return null;

        var rowValues = new HashSet<Guid>();
        foreach (var value in row.Values)
        {
            var id = AsGuid(value);
            if (id != Guid.Empty) rowValues.Add(id);
        }

        foreach (var pack in packs)
        {
            var item = TryGuid(pack, "Item");
            var qty = Decimal(pack, "QtyInBaseUnit");
            if (item != Guid.Empty && qty > 0m && rowValues.Contains(item)) return qty;
        }
        return null;
    }

    /// <summary>
    /// Перевод по коэффициентам к базовой единице вида. Копия арифметики из
    /// сервиса Common, и не по небрежности: наружу из модели торчит только
    /// СГЕНЕРИРОВАННЫЙ контракт с инстанс-методами, статический помощник чужой
    /// модели не виден. Правило при этом одно на обе копии.
    /// </summary>
    private static decimal? ByRatio(
        decimal quantity, Guid fromClass, decimal fromRatio, Guid toClass, decimal toRatio)
    {
        if (fromClass == Guid.Empty || toClass == Guid.Empty) return null;
        if (fromClass != toClass) return null;
        if (fromRatio <= 0m || toRatio <= 0m) return null;
        return quantity * fromRatio / toRatio;
    }

    private static decimal RoundQty(decimal value, int scale)
        => Math.Round(value, Math.Max(0, Math.Min(scale, 28)), MidpointRounding.AwayFromZero);

    private static int FallbackScale() => GlobalConstants.Get<int?>("QuantityScale") ?? 3;

    private static Guid AsGuid(object? value)
    {
        if (value is null) return Guid.Empty;
        return value is Guid g ? g : Guid.TryParse(value.ToString(), out var p) ? p : Guid.Empty;
    }

    /// <summary>Перевод по виду величины — теми же правилами, что и в сервисе Common.</summary>
    private static async Task<decimal?> ByClassAsync(
        IRowReader reader, decimal quantity, Guid fromUnit, Guid toUnit, CancellationToken ct)
    {
        var from = await UnitRowAsync(reader, fromUnit, ct);
        var to = await UnitRowAsync(reader, toUnit, ct);
        if (from == null || to == null) return null;

        return ByRatio(
            quantity,
            TryGuid(from, "UnitClass"), Decimal(from, "RatioToBase"),
            TryGuid(to, "UnitClass"), Decimal(to, "RatioToBase"));
    }

    private static async Task<IReadOnlyDictionary<string, object?>?> UnitRowAsync(
        IRowReader reader, Guid unit, CancellationToken ct)
        => (await reader.ReadAsync("UnitOfMeasure", $"MetaId = '{unit:D}'", ct)).FirstOrDefault();

    private static async Task<int> UnitPrecisionAsync(IRowReader reader, Guid unit, CancellationToken ct)
    {
        var row = await UnitRowAsync(reader, unit, ct);
        if (row != null && row.TryGetValue("DecimalPlaces", out var v) && v != null)
            return Convert.ToInt32(v, CultureInfo.InvariantCulture);
        return FallbackScale();
    }

    private static Guid TryGuid(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v is null) return Guid.Empty;
        return v is Guid g ? g : Guid.TryParse(v.ToString(), out var p) ? p : Guid.Empty;
    }

    private static decimal Decimal(IReadOnlyDictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var v) && v != null
            ? Convert.ToDecimal(v, CultureInfo.InvariantCulture)
            : 0m;
}
