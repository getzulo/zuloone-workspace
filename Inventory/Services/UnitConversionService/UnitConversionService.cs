using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
// ЯВНЫЙ using, хотя ZuloOne.Runtime.Services уже есть в global usings скриптов:
// сгенерированный исходник контракта копирует только НЕ-глобальные using'и файла,
// а сборка контрактов компилируется без script framework. Без этой строки
// IQuantityConverter/QuantityConversionRequest в сигнатуре не разрешатся (CS0246),
// и упадёт НЕ один сервис, а ВЕСЬ реестр: ServiceRegistry штампует ошибку на
// КАЖДЫЙ сервис и отдаёт пустой реестр.
using ZuloOne.Runtime.Services;

// Сервис "UnitConversionService": контракт IUnitConversionService плюс
// ПЛАТФОРМЕННЫЙ контракт IQuantityConverter. Единая логика перевода единиц
// измерения по справочнику UnitConversion (1 FromUnit = Factor × ToUnit). Знает
// обратное правило (только From→To → To→From считается делением), тождество, и
// ОКРУГЛЕНИЕ по точности целевой единицы (UnitOfMeasure.DecimalPlaces).
// Сценарий: спецификация «2 г колбасы» на 10 бутербродов = 20 г → в базовой
// единице товара (кг, 3 знака) = 0.020; булки (шт, 0 знаков) 1×10 = 10; плёнка
// (м, 2 знака) 0.15×10 = 1.50. Точность берётся с единицы, глобальная
// QuantityScale — запасной вариант.
//
// ДВА ВХОДА, и это не дублирование:
//   • ConvertAsync(decimal, Guid, Guid) и соседи — прикладной вход (BomService):
//     данные читаются типизированными IDictionaryManager<T> на своём соединении;
//   • ConvertAsync(QuantityConversionRequest) — платформенный вход, который зовёт
//     QuantityNormalizer ИЗ СЕРЕДИНЫ записи строки. Там своё соединение открывать
//     нельзя (внешняя транзакция промоутнется в MSDTC, а Suppress-скоуп на SQL
//     Server ЗАБЛОКИРУЕТСЯ на строках, залоченных текущей транзакцией), поэтому
//     правила читаются через request.Reader — соединение, которое платформа уже
//     держит, внутри её же транзакции.
public partial class UnitConversionService : IQuantityConverter
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
        return u?.DecimalPlaces ?? DefaultScale();
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

    // ───────────────── платформенный вход: IQuantityConverter ─────────────────

    /// <summary>
    /// Количество строки в БАЗОВОЙ единице товара, округлённое по точности целевой
    /// единицы, но не тоньше, чем умеет хранить колонка-приёмник (TargetScale).
    ///
    /// ТОЖДЕСТВО ОТВЕЧАЕТСЯ ЗДЕСЬ, а не отсекается платформой: короткого замыкания
    /// на FromUnit == ToUnit у нормализатора нет НАМЕРЕННО — округление это знание
    /// конвертера, а не движка. Вернуть null на паре «единица сама в себя» значило
    /// бы 400 на КАЖДУЮ строку, введённую в базовой единице товара, то есть на
    /// подавляющее большинство строк.
    ///
    /// null — ровно «правила нет»; отказывать в записи решает платформа, поэтому
    /// исключений отсюда не летит.
    ///
    /// Все чтения — через request.Reader; почему не своим соединением, см. шапку класса.
    /// </summary>
    public async Task<decimal?> ConvertAsync(
        QuantityConversionRequest request, System.Threading.CancellationToken ct = default)
    {
        decimal converted;
        if (request.FromUnit == request.ToUnit)
        {
            converted = request.Quantity;
        }
        else
        {
            var direct = await RuleFactorAsync(request.Reader, request.FromUnit, request.ToUnit, ct);
            if (direct.HasValue)
            {
                converted = request.Quantity * direct.Value;
            }
            else
            {
                // Обратное правило ДЕЛИТ, а не умножает на 1/Factor: 1/3 в decimal
                // потеряло бы разряды ещё до умножения на количество.
                var inverse = await RuleFactorAsync(request.Reader, request.ToUnit, request.FromUnit, ct);
                if (!inverse.HasValue || inverse.Value == 0m) return null;
                converted = request.Quantity / inverse.Value;
            }
        }

        // Точность единицы, ограниченная масштабом колонки: единица с DecimalPlaces = 6,
        // ложась в DECIMAL(18,4), была бы молча обрезана провайдером.
        var scale = Math.Min(await ReaderPrecisionAsync(request.Reader, request.ToUnit, ct), request.TargetScale);
        return Math.Round(converted, Math.Max(0, Math.Min(scale, 28)), MidpointRounding.AwayFromZero);
    }

    /// <summary>Factor прямого правила fromUnit → toUnit на соединении платформы;
    /// null — правила нет.</summary>
    private static async Task<decimal?> RuleFactorAsync(
        IRowReader reader, Guid fromUnit, Guid toUnit, System.Threading.CancellationToken ct)
    {
        var rows = await reader.ReadAsync(
            "UnitConversion", $"FromUnit = '{fromUnit:D}' AND ToUnit = '{toUnit:D}'", ct);
        foreach (var row in rows)
        {
            if (row.TryGetValue("Factor", out var value) && value != null)
                return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        return null;
    }

    /// <summary>То же, что PrecisionAsync, но на соединении платформы.</summary>
    private static async Task<int> ReaderPrecisionAsync(
        IRowReader reader, Guid unit, System.Threading.CancellationToken ct)
    {
        var rows = await reader.ReadAsync("UnitOfMeasure", $"MetaId = '{unit:D}'", ct);
        foreach (var row in rows)
        {
            if (row.TryGetValue("DecimalPlaces", out var value) && value != null)
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        return DefaultScale();
    }

    /// <summary>Глобальная точность количеств — запасной вариант, когда единица
    /// своей не объявила.</summary>
    private static int DefaultScale() => GlobalConstants.Get<int?>("QuantityScale") ?? 3;
}
