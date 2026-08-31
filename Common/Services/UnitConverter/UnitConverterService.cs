using System;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Generated;

// Перевод количеств между единицами измерения ОДНОГО вида величины.
//
// Модель: у каждой единицы есть вид величины (UnitClass) и коэффициент к базовой
// единице этого вида (RatioToBase — сколько базовых в одной этой). Перевод —
// qty × from.RatioToBase / to.RatioToBase. Отсюда три свойства, которых не было
// у попарных правил:
//   • ТРАНЗИТИВНОСТЬ БЕСПЛАТНА: тонна → грамм считается без правила «тонна-грамм»,
//     потому что обе единицы выражены через грамм;
//   • N ЧИСЕЛ ВМЕСТО N² ПРАВИЛ, и противоречивую тройку (тонна→грамм ≠
//     тонна→килограмм × килограмм→грамм) стало нечем выразить;
//   • ПЕРЕВОД МЕЖДУ ВИДАМИ НЕВОЗМОЖЕН ПО ПОСТРОЕНИЮ — «килограмм в метр» это не
//     «правило не найдено», а разные величины.
//
// Живёт в Common (слой 1) сознательно: на Common ссылаются все модели, поэтому
// пересчёт стал доступен и базовым — Tax, Accounting, Organization, — которые
// раньше не могли перевести единицы вообще.
//
// Товарные упаковки («коробка ЭТОГО товара = 12 штук») здесь принципиально
// отсутствуют: они зависят от номенклатуры, а номенклатура живёт слоем выше.
// Ими занимается ItemQuantityConverter в Inventory.
public partial class UnitConverter
{
    private readonly IDictionaryManager<UnitOfMeasure> _units;

    public UnitConverter(IDictionaryManager<UnitOfMeasure> units) => _units = units;

    /// <summary>
    /// Перевод количества между единицами одного вида; null — перевести нечем:
    /// разные виды величины, либо у единицы нет коэффициента (так задаются
    /// упаковки вроде коробки, у которых он зависит от товара).
    ///
    /// Тождество возвращает количество, а НЕ null: единица сама в себя — это
    /// корректный перевод, а не отсутствие правила.
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
    /// Чистая арифметика перевода — без обращений к базе. Вынесена отдельно,
    /// чтобы платформенный конвертер (который обязан читать только через
    /// соединение платформы) считал ТЕМ ЖЕ выражением, а не своей копией.
    /// </summary>
    public static decimal? ConvertByRatio(
        decimal quantity, Guid fromClass, decimal fromRatio, Guid toClass, decimal toRatio)
    {
        if (fromClass == Guid.Empty || toClass == Guid.Empty) return null;
        if (fromClass != toClass) return null;              // масса в длину не переводится
        if (fromRatio <= 0m || toRatio <= 0m) return null;  // коэффициента нет (упаковка)

        return quantity * fromRatio / toRatio;
    }

    /// <summary>Перевод с округлением до точности целевой единицы.</summary>
    public async Task<decimal?> ConvertRoundedAsync(decimal quantity, Guid fromUnit, Guid toUnit)
    {
        var converted = await ConvertAsync(quantity, fromUnit, toUnit);
        if (!converted.HasValue) return null;
        return Round(converted.Value, await PrecisionAsync(toUnit));
    }

    /// <summary>Знаков после запятой у единицы; иначе глобальная QuantityScale.</summary>
    public async Task<int> PrecisionAsync(Guid unit)
    {
        var u = await _units.GetRecordAsync(unit);
        return u?.DecimalPlaces ?? DefaultScale();
    }

    /// <summary>
    /// Коэффициент перевода — для отображения. Для пересчёта количеств зовите
    /// ConvertAsync: он не теряет разряды на промежуточном делении.
    /// </summary>
    public async Task<decimal?> FactorAsync(Guid fromUnit, Guid toUnit)
        => fromUnit == toUnit ? 1m : await ConvertAsync(1m, fromUnit, toUnit);

    /// <summary>Округление количества — одно на все входы.</summary>
    public static decimal Round(decimal value, int scale)
        => Math.Round(value, Math.Max(0, Math.Min(scale, 28)), MidpointRounding.AwayFromZero);

    public static int DefaultScale() => GlobalConstants.Get<int?>("QuantityScale") ?? 3;
}
