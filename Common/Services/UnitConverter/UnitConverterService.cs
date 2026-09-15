using System;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Generated;

// Convert quantities between units of measure of ONE quantity class.
//
// Model: each unit has a quantity class (UnitClass) and a ratio to the base
// unit of that class (RatioToBase — how many base units in one of this).
// Conversion is qty × from.RatioToBase / to.RatioToBase. Hence three properties
// pairwise rules did not have:
//   • TRANSITIVITY IS FREE: tonne → gram computes without a "tonne-gram" rule,
//     because both units are expressed through the gram;
//   • N NUMBERS INSTEAD OF N² RULES, and a contradictory triple (tonne→gram ≠
//     tonne→kilogram × kilogram→gram) has nothing to express it with;
//   • CROSS-CLASS CONVERSION IS IMPOSSIBLE BY CONSTRUCTION — "kilogram to
//     metre" is not "rule not found", it is different quantities.
//
// Lives in Common (layer 1) on purpose: every model references Common, so
// conversion became available to base models too — Tax, Accounting,
// Organization — which previously could not convert units at all.
//
// Item packs ("a box of THIS item = 12 pieces") are deliberately absent here:
// they depend on the item, and items live a layer above. ItemQuantityConverter
// in Inventory handles those.
public partial class UnitConverter
{
    private readonly IDictionaryManager<UnitOfMeasure> _units;

    public UnitConverter(IDictionaryManager<UnitOfMeasure> units) => _units = units;

    /// <summary>
    /// Convert a quantity between units of one class; null — nothing to convert
    /// with: different quantity classes, or the unit has no ratio (that is how
    /// packs like a box are specified, whose ratio depends on the item).
    ///
    /// Identity returns the quantity, NOT null: a unit into itself is a valid
    /// conversion, not a missing rule.
    /// </summary>
    public async Task<decimal?> ConvertAsync(decimal quantity, Guid fromUnit, Guid toUnit)
    {
        if (fromUnit == toUnit) return quantity;

        var from = await _units.GetRecordAsync(fromUnit);
        var to = await _units.GetRecordAsync(toUnit);
        if (from == null || to == null) return null;

        return ConvertByRatio(quantity, from.UnitClass, from.RatioToBase, to.UnitClass, to.RatioToBase);
    }

    /// <summary>
    /// Pure conversion arithmetic — no database calls. Extracted so the platform
    /// converter (which must read only through the platform connection) computes
    /// by THE SAME expression, not its own copy.
    /// </summary>
    public static decimal? ConvertByRatio(
        decimal quantity, Guid fromClass, decimal fromRatio, Guid toClass, decimal toRatio)
    {
        if (fromClass == Guid.Empty || toClass == Guid.Empty) return null;
        if (fromClass != toClass) return null;              // mass does not convert to length
        if (fromRatio <= 0m || toRatio <= 0m) return null;  // no ratio (a pack)

        return quantity * fromRatio / toRatio;
    }

    /// <summary>Conversion rounded to the target unit's precision.</summary>
    public async Task<decimal?> ConvertRoundedAsync(decimal quantity, Guid fromUnit, Guid toUnit)
    {
        var converted = await ConvertAsync(quantity, fromUnit, toUnit);
        if (!converted.HasValue) return null;
        return Round(converted.Value, await PrecisionAsync(toUnit));
    }

    /// <summary>Decimal places of the unit; otherwise the global QuantityScale.</summary>
    public async Task<int> PrecisionAsync(Guid unit)
    {
        var u = await _units.GetRecordAsync(unit);
        return u?.DecimalPlaces ?? DefaultScale();
    }

    /// <summary>
    /// Conversion factor — for display. For quantity conversion call
    /// ConvertAsync: it does not lose digits on an intermediate division.
    /// </summary>
    public async Task<decimal?> FactorAsync(Guid fromUnit, Guid toUnit)
        => fromUnit == toUnit ? 1m : await ConvertAsync(1m, fromUnit, toUnit);

    /// <summary>Quantity rounding — one for every entry point.</summary>
    public static decimal Round(decimal value, int scale)
        => Math.Round(value, Math.Max(0, Math.Min(scale, 28)), MidpointRounding.AwayFromZero);

    public static int DefaultScale() => GlobalConstants.Get<int?>("QuantityScale") ?? 3;
}
