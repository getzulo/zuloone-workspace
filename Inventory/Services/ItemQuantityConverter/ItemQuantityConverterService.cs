using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Generated;
// EXPLICIT using, even though ZuloOne.Runtime.Services is already in script global usings:
// the generated contract source copies only the file's NON-global usings,
// and the contract assembly compiles without the script framework. Without this line
// IQuantityConverter/QuantityConversionRequest in the signature will not resolve (CS0246),
// and it is not one service that fails — it is the ENTIRE registry.
using ZuloOne.Runtime.Services;

// Convert a document-line quantity into the ITEM's base unit.
//
// Resolution order is specific-to-general, and that order matters:
//   1. IDENTITY. The line unit matches the item's base unit — return
//      the quantity. The platform deliberately does NOT short-circuit here, and
//      null would refuse every line entered in the base
//      unit, i.e. the vast majority of lines.
//   2. ITEM PACKAGING (ItemUnit). A "box" without an item is not a quantity: one
//      item has 12 pieces in it, another has 6. So packaging is asked FIRST
//      and only for the item on the line.
//   3. UNIT CLASS (UnitClass + RatioToBase). Kilograms to grams are the same for
//      any item; the item is not needed here.
//   4. Otherwise null — "no rule". NO EXCEPTIONS ARE THROWN FROM HERE: refusing
//      the save is the platform's decision, the converter does not make it.
//
// Lives in Inventory, not Common, because packaging references Item, and
// layer 1 cannot reference it. That is the cost of item packagings: class
// conversion is available to every model (UnitConverter in Common), item
// conversion only from layer 2.
//
// ALL reads go through request.Reader. Opening our own connection is forbidden:
// the outer transaction would promote to MSDTC (absent on Linux), and a
// suppress scope on SQL Server would block on rows locked by the current
// transaction — a test that seeded packaging inside its own rollback would
// hang instead of fail.
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

    // ───────────────── application entry ─────────────────

    /// <summary>
    /// Item quantity in its base unit; null — nothing to convert with.
    /// First this item's packaging, then the shared unit class.
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

    /// <summary>Same, rounded to the item's base-unit precision.</summary>
    public async Task<decimal?> ToBaseRoundedAsync(Guid item, decimal quantity, Guid fromUnit)
    {
        var converted = await ToBaseAsync(item, quantity, fromUnit);
        if (!converted.HasValue) return null;

        var itemRecord = await _items.GetRecordAsync(item);
        var baseUnit = itemRecord == null ? Guid.Empty : itemRecord.UnitOfMeasure;
        var unit = baseUnit == Guid.Empty ? null : await _units.GetRecordAsync(baseUnit);
        return RoundQty(converted.Value, unit?.DecimalPlaces ?? FallbackScale());
    }

    // ───────────────── platform entry: IQuantityConverter ─────────────────

    public async Task<decimal?> ConvertAsync(QuantityConversionRequest request, CancellationToken ct = default)
    {
        decimal? converted;

        if (request.FromUnit == request.ToUnit)
        {
            converted = request.Quantity;
        }
        else
        {
            // The item-reference field name is not known in advance (on an order
            // line it is Item, on a BOM line — Component), and it is not on the request.
            // Approach from the other side: take packagings of this UNIT and check
            // whether their item appears among the row values. A match is
            // unambiguous — packaging is bound to the (item, unit) pair.
            var packQty = await PackFactorAsync(request.Reader, request.Row, request.FromUnit, ct);
            converted = packQty.HasValue
                ? request.Quantity * packQty.Value
                : await ByClassAsync(request.Reader, request.Quantity, request.FromUnit, request.ToUnit, ct);
        }

        if (!converted.HasValue) return null;

        // Unit precision, capped by the target column scale: a unit with
        // six places landing in DECIMAL(18,4) would be silently truncated by the driver.
        var scale = Math.Min(
            await UnitPrecisionAsync(request.Reader, request.ToUnit, ct), request.TargetScale);
        return RoundQty(converted.Value, scale);
    }

    /// <summary>How many of the item's base units are in one package; null — no packaging.</summary>
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
    /// Conversion by ratios to the class base unit. A copy of the Common
    /// service arithmetic, and not by sloppiness: only the GENERATED contract
    /// with instance methods is visible outside the model; a static helper of
    /// another model is not. The rule is the same on both copies.
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

    /// <summary>Conversion by unit class — the same rules as in the Common service.</summary>
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
