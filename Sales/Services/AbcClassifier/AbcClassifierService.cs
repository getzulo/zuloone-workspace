#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

public partial class AbcClassifier
{
    private static readonly Guid RevenueRegister = Guid.Parse("103cdb23-2d49-4c42-9bf3-3c5a8ce46cdc");
    private static readonly Guid InventoryValueRegister = Guid.Parse("7e0b4d85-1a6f-4c9d-8e5b-8f1a4c7d0e60");
    private static readonly Guid StockRegister = Guid.Parse("83559331-ac7f-46da-87a8-7da599ef6f41");

    private readonly IDictionaryManager<AbcProfile> _profiles;
    private readonly IDictionaryManager<AbcClassBand> _bands;
    private readonly IRegisterMovementService _movements;
    private readonly IInformationRegisterService _info;
    private readonly AnalyticSetService _analytics;

    public AbcClassifier(
        IDictionaryManager<AbcProfile> profiles,
        IDictionaryManager<AbcClassBand> bands,
        IRegisterMovementService movements,
        IInformationRegisterService info,
        AnalyticSetService analytics)
    {
        _profiles = profiles;
        _bands = bands;
        _movements = movements;
        _info = info;
        _analytics = analytics;
    }

    /// <summary>Score from registers, assign bands, write SliceLast. Returns rows written.</summary>
    public async Task<int> RecalcAsync(Guid profileId, DateTime asOf)
    {
        var profile = await LoadProfileAsync(profileId);
        var scores = await ScoreFromRegistersAsync(profile, asOf);
        IReadOnlyDictionary<Guid, decimal[]>? series = null;
        if (profile.Measure == "Revenue" && profile.XyzMethod != "None")
            series = await RevenueSeriesAsync(profile, asOf);
        return await WriteAsync(profile, asOf, scores, series);
    }

    /// <summary>Nightly path: every live profile, same RecalcAsync as the button.</summary>
    public async Task<int> RecalcAllEnabledAsync(DateTime asOf)
    {
        var n = 0;
        foreach (var profile in await _profiles.GetRecordsAsync("1 = 1"))
        {
            if (profile.IsDisabled) continue;
            n += await RecalcAsync(profile.MetaId, asOf);
        }
        return n;
    }

    /// <summary>Same write path with caller-supplied scores (Pareto tests, no HTTP).</summary>
    public async Task<int> RecalcWithScoresAsync(
        Guid profileId,
        DateTime asOf,
        IReadOnlyDictionary<Guid, decimal> scores)
    {
        var profile = await LoadProfileAsync(profileId);
        return await WriteAsync(profile, asOf, scores, series: null);
    }

    private async Task<AbcProfile> LoadProfileAsync(Guid profileId)
    {
        var profile = await _profiles.GetRecordAsync(profileId)
            ?? throw new InvalidOperationException("Профиль ABC не найден");
        if (profile.IsDisabled)
            throw new InvalidOperationException("Профиль ABC отключён");
        if (profile.Subject == "Customer"
            && (profile.Measure == "StockQty" || profile.Measure == "InventoryValue"))
            throw new InvalidOperationException("Показатель остатка нельзя считать по покупателю");
        return profile;
    }

    private async Task<int> WriteAsync(
        AbcProfile profile,
        DateTime asOf,
        IReadOnlyDictionary<Guid, decimal> scores,
        IReadOnlyDictionary<Guid, decimal[]>? series)
    {
        var bands = (await _bands.GetRecordsAsync("1 = 1"))
            .Where(b => b.Profile == profile.MetaId)
            .ToList();
        var abcBands = Ordered(bands, "Abc");
        var xyzBands = Ordered(bands, "Xyz");
        ValidateAxis(abcBands, "ABC");
        if (profile.XyzMethod != "None")
            ValidateAxis(xyzBands, "XYZ");

        var positive = scores.Where(kv => kv.Value > 0m).ToList();
        var total = positive.Sum(kv => kv.Value);
        var ranked = positive.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).ToList();

        var existing = await _info.SliceLastAsync(
            "AbcClassification",
            asOf,
            new Dictionary<string, object?> { ["Profile"] = profile.MetaId });
        var prev = new Dictionary<Guid, Dictionary<string, object?>>();
        foreach (var row in existing)
        {
            var subject = AsGuid(row, "Subject");
            if (subject != Guid.Empty) prev[subject] = row;
        }

        var written = 0;
        var running = 0m;
        var index = 0;
        foreach (var (subject, score) in ranked)
        {
            var shareOfTotal = total == 0m ? 0m : 100m * score / total;
            var abcKey = profile.AbcMethod switch
            {
                "Absolute" => score,
                "Rank" => ranked.Count == 0 ? 0m : 100m * index / ranked.Count,
                _ => running,
            };
            var abc = Pick(abcBands, abcKey);
            running += shareOfTotal;
            index++;

            decimal cv = 0m;
            var xyz = "";
            if (profile.XyzMethod != "None" && series != null && series.TryGetValue(subject, out var buckets))
            {
                cv = profile.XyzMethod == "ZeroBuckets"
                    ? (buckets.Length == 0 ? 0m : buckets.Count(v => v == 0m) / (decimal)buckets.Length)
                    : CoefficientOfVariation(buckets);
                xyz = cv < 0m ? xyzBands[^1].ClassCode : Pick(xyzBands, cv);
            }

            prev.TryGetValue(subject, out var old);
            var manualAbc = Flag(old, "ManualAbc");
            var manualXyz = Flag(old, "ManualXyz");
            if (manualAbc) abc = Str(old, "AbcClass");
            if (manualXyz) xyz = Str(old, "XyzClass");

            await _info.SetAsync(
                "AbcClassification",
                asOf,
                new Dictionary<string, object?>
                {
                    ["Profile"] = profile.MetaId,
                    ["Subject"] = subject,
                },
                new Dictionary<string, object?>
                {
                    ["AbcClass"] = abc,
                    ["XyzClass"] = xyz,
                    ["Score"] = score,
                    ["Share"] = shareOfTotal,
                    ["Cv"] = cv < 0m ? 0m : cv,
                    ["ManualAbc"] = manualAbc,
                    ["ManualXyz"] = manualXyz,
                });
            written++;
        }

        return written;
    }

    private async Task<Dictionary<Guid, decimal>> ScoreFromRegistersAsync(AbcProfile profile, DateTime asOf)
    {
        if (profile.Measure == "Revenue")
            return await SumRevenueAsync(profile, asOf);
        if (profile.Measure == "StockQty")
            return await SumStockAsync();
        return await SumInventoryValueAsync();
    }

    private async Task<Dictionary<Guid, decimal>> SumRevenueAsync(AbcProfile profile, DateTime asOf)
    {
        var analytic = profile.Subject == "Customer" ? "Customer" : "Item";
        var from = asOf.Date.AddMonths(-profile.WindowMonths);
        var to = asOf.Date;
        var movements = await _movements.QueryMovementsAsync(
            RevenueRegister,
            $"[MovementDate] >= '{from:yyyy-MM-dd HH:mm:ss}' AND [MovementDate] < '{to:yyyy-MM-dd HH:mm:ss}'");
        return await SumByAnalyticAsync(movements, analytic, "Amount");
    }

    private async Task<Dictionary<Guid, decimal[]>> RevenueSeriesAsync(AbcProfile profile, DateTime asOf)
    {
        var analytic = profile.Subject == "Customer" ? "Customer" : "Item";
        var n = profile.BucketCount;
        var from = asOf.Date.AddMonths(-profile.WindowMonths);
        var to = asOf.Date;
        var span = Math.Max(1L, (to - from).Ticks);
        var movements = await _movements.QueryMovementsAsync(
            RevenueRegister,
            $"[MovementDate] >= '{from:yyyy-MM-dd HH:mm:ss}' AND [MovementDate] < '{to:yyyy-MM-dd HH:mm:ss}'");
        if (movements.Count == 0) return new Dictionary<Guid, decimal[]>();

        var setIds = movements.Select(m => AsGuid(m, "AnalyticSetMetaId")).Where(id => id != Guid.Empty).Distinct().ToList<Guid>();
        var sets = await _analytics.ExpandAsync(setIds);
        var series = new Dictionary<Guid, decimal[]>();
        foreach (var movement in movements)
        {
            var setId = AsGuid(movement, "AnalyticSetMetaId");
            if (setId == Guid.Empty || !sets.TryGetValue(setId, out var values)) continue;
            var subject = ValueGuid(values, analytic);
            if (subject == Guid.Empty) continue;
            if (!series.TryGetValue(subject, out var buckets))
                series[subject] = buckets = new decimal[n];
            var date = AsDate(movement, "MovementDate");
            var idx = (int)((date - from).Ticks * n / span);
            if (idx < 0) idx = 0;
            if (idx >= n) idx = n - 1;
            buckets[idx] += Decimal(movement, "Amount");
        }
        return series;
    }

    private async Task<Dictionary<Guid, decimal>> SumStockAsync()
    {
        var rows = await _movements.QueryBalancesAsync(StockRegister);
        var scores = new Dictionary<Guid, decimal>();
        foreach (var row in rows)
        {
            var item = AsGuid(row, "Item");
            if (item == Guid.Empty) continue;
            scores.TryGetValue(item, out var cur);
            scores[item] = cur + Decimal(row, "Qty");
        }
        return scores;
    }

    private async Task<Dictionary<Guid, decimal>> SumInventoryValueAsync()
    {
        var rows = await _movements.QueryBalancesAsync(InventoryValueRegister);
        return await SumByAnalyticAsync(rows, "Item", "Value");
    }

    private async Task<Dictionary<Guid, decimal>> SumByAnalyticAsync(
        List<Dictionary<string, object?>> rows, string analytic, string resource)
    {
        var scores = new Dictionary<Guid, decimal>();
        if (rows.Count == 0) return scores;
        var setIds = rows.Select(m => AsGuid(m, "AnalyticSetMetaId")).Where(id => id != Guid.Empty).Distinct().ToList<Guid>();
        var sets = await _analytics.ExpandAsync(setIds);
        foreach (var row in rows)
        {
            var direct = AsGuid(row, analytic);
            Guid subject;
            if (direct != Guid.Empty)
            {
                subject = direct;
            }
            else
            {
                var setId = AsGuid(row, "AnalyticSetMetaId");
                if (setId == Guid.Empty || !sets.TryGetValue(setId, out var values)) continue;
                subject = ValueGuid(values, analytic);
            }
            if (subject == Guid.Empty) continue;
            scores.TryGetValue(subject, out var cur);
            scores[subject] = cur + Decimal(row, resource);
        }
        return scores;
    }

    private static List<AbcClassBand> Ordered(IEnumerable<AbcClassBand> bands, string axis)
        => bands.Where(b => string.Equals(b.Axis, axis, StringComparison.OrdinalIgnoreCase))
            .OrderBy(b => b.BoundFrom)
            .ThenBy(b => b.ClassCode)
            .ToList();

    private static void ValidateAxis(List<AbcClassBand> bands, string axis)
    {
        if (bands.Count == 0)
            throw new InvalidOperationException($"Нет полос {axis} у профиля");
        for (var i = 0; i < bands.Count; i++)
        {
            var cur = bands[i];
            if (i == bands.Count - 1) continue;
            var next = bands[i + 1];
            if (!HasUpper(cur))
                throw new InvalidOperationException($"У полосы {axis} «{cur.ClassCode}» должна быть верхняя граница");
            if (cur.BoundTo != next.BoundFrom)
                throw new InvalidOperationException($"Полосы {axis} «{cur.ClassCode}» и «{next.ClassCode}» пересекаются или имеют разрыв");
        }
    }

    private static string Pick(List<AbcClassBand> bands, decimal value)
    {
        foreach (var band in bands)
        {
            if (value < band.BoundFrom) continue;
            if (HasUpper(band) && value >= band.BoundTo) continue;
            return band.ClassCode;
        }
        return bands[^1].ClassCode;
    }

    private static bool HasUpper(AbcClassBand band) => band.BoundTo != 0m;

    private static decimal CoefficientOfVariation(decimal[] xs)
    {
        if (xs.Length == 0) return -1m;
        var mean = xs.Sum() / xs.Length;
        if (mean == 0m) return -1m;
        var variance = xs.Sum(x => (x - mean) * (x - mean)) / xs.Length;
        return (decimal)Math.Sqrt((double)variance) / mean;
    }

    private static Guid ValueGuid(IReadOnlyDictionary<string, string> values, string analytic)
        => values.TryGetValue(analytic, out var v) && Guid.TryParse(v, out var g) ? g : Guid.Empty;

    private static Guid AsGuid(IDictionary<string, object?> row, string column)
    {
        if (!row.TryGetValue(column, out var v) || v is null) return Guid.Empty;
        return v is Guid g ? g : Guid.TryParse(v.ToString(), out var p) ? p : Guid.Empty;
    }

    private static decimal Decimal(IDictionary<string, object?> row, string column)
        => row.TryGetValue(column, out var v) && v != null
            ? Convert.ToDecimal(v, CultureInfo.InvariantCulture)
            : 0m;

    private static DateTime AsDate(IDictionary<string, object?> row, string column)
        => row.TryGetValue(column, out var v) && v != null
            ? Convert.ToDateTime(v, CultureInfo.InvariantCulture)
            : DateTime.MinValue;

    private static bool Flag(Dictionary<string, object?>? row, string column)
    {
        if (row is null || !row.TryGetValue(column, out var v) || v is null) return false;
        if (v is bool b) return b;
        return v.ToString() == "1" || string.Equals(v.ToString(), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string Str(Dictionary<string, object?>? row, string column)
    {
        if (row is null || !row.TryGetValue(column, out var v) || v is null) return "";
        return v.ToString() ?? "";
    }
}
