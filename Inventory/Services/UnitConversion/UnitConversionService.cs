using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Generated;

// Сервис "UnitConversionService": контракт IUnitConversionService. Единая логика
// перевода единиц измерения по справочнику UnitConversion (1 FromUnit = Factor ×
// ToUnit). Знает обратное правило (только From→To → To→From считается делением),
// тождество, и ОКРУГЛЕНИЕ по точности целевой единицы (UnitOfMeasure.DecimalPlaces).
// Сценарий: спецификация «2 г колбасы» на 10 бутербродов = 20 г → в базовой
// единице товара (кг, 3 знака) = 0.020; булки (шт, 0 знаков) 1×10 = 10; плёнка
// (м, 2 знака) 0.15×10 = 1.50. Точность берётся с единицы, глобальная
// QuantityScale — запасной вариант. Данные — типизированные IDictionaryManager<T>.
public partial class UnitConversionService
{
    private readonly IDictionaryManager<UnitConversion> _conversions;
    private readonly IDictionaryManager<UnitOfMeasure> _units;

    public UnitConversionService(IDictionaryManager<UnitConversion> conversions, IDictionaryManager<UnitOfMeasure> units)
    {
        _conversions = conversions;
        _units = units;
    }

    /// <summary>Перевести количество fromUnit → toUnit; null, если правила нет.
    /// Прямое правило умножает на Factor, обратное — ДЕЛИТ на Factor (точно).</summary>
    public async Task<decimal?> ConvertAsync(decimal quantity, Guid fromUnit, Guid toUnit)
    {
        if (fromUnit == toUnit) return quantity;

        var direct = (await _conversions.GetRecordsAsync($"FromUnit = '{fromUnit}' AND ToUnit = '{toUnit}'")).FirstOrDefault();
        if (direct != null) return quantity * direct.Factor;

        var inverse = (await _conversions.GetRecordsAsync($"FromUnit = '{toUnit}' AND ToUnit = '{fromUnit}'")).FirstOrDefault();
        if (inverse != null && inverse.Factor != 0m) return quantity / inverse.Factor;

        return null;
    }

    /// <summary>Перевести и округлить до точности целевой единицы (DecimalPlaces,
    /// иначе глобальная QuantityScale). Это и есть «сколько списать со склада»:
    /// напр. 20 г колбасы → 0.020 кг.</summary>
    public async Task<decimal?> ConvertRoundedAsync(decimal quantity, Guid fromUnit, Guid toUnit)
    {
        var converted = await ConvertAsync(quantity, fromUnit, toUnit);
        if (!converted.HasValue) return null;
        var scale = await PrecisionAsync(toUnit);
        return Math.Round(converted.Value, scale, MidpointRounding.AwayFromZero);
    }

    /// <summary>Точность (знаков после запятой) единицы измерения: её DecimalPlaces,
    /// иначе глобальная настройка QuantityScale (по умолчанию 3).</summary>
    public async Task<int> PrecisionAsync(Guid unit)
    {
        var u = await _units.GetRecordAsync(unit);
        return u?.DecimalPlaces ?? (GlobalConstants.Get<int?>("QuantityScale") ?? 3);
    }

    /// <summary>Коэффициент перевода fromUnit → toUnit (для отображения); null, если
    /// правила нет. Для точного пересчёта количеств используйте ConvertAsync.</summary>
    public async Task<decimal?> FactorAsync(Guid fromUnit, Guid toUnit)
    {
        if (fromUnit == toUnit) return 1m;

        var direct = (await _conversions.GetRecordsAsync($"FromUnit = '{fromUnit}' AND ToUnit = '{toUnit}'")).FirstOrDefault();
        if (direct != null) return direct.Factor;

        var inverse = (await _conversions.GetRecordsAsync($"FromUnit = '{toUnit}' AND ToUnit = '{fromUnit}'")).FirstOrDefault();
        return inverse != null && inverse.Factor != 0m ? 1m / inverse.Factor : (decimal?)null;
    }
}
