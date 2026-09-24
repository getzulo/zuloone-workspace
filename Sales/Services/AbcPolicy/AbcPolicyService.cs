#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Матрица AX…CZ — строки AbcPolicyCell, не буквы в коде. Класс уже лежит
// в AbcClassification. Здесь только «держать N дней расхода» или «под заказ».
// Расход — базовое количество счетов Реализовано/Отгружен за окно профиля.
// Остаток — сумма Stock.Qty. Заказ поставщику и движения склада не создаются.
public partial class AbcPolicy
{
    private static readonly Guid StockRegister = Guid.Parse("83559331-ac7f-46da-87a8-7da599ef6f41");

    private readonly IDictionaryManager<AbcProfile> _profiles;
    private readonly IDictionaryManager<AbcPolicyCell> _cells;
    private readonly IInformationRegisterService _info;
    private readonly IRegisterMovementService _movements;
    private readonly ISqlService _sql;

    public AbcPolicy(
        IDictionaryManager<AbcProfile> profiles,
        IDictionaryManager<AbcPolicyCell> cells,
        IInformationRegisterService info,
        IRegisterMovementService movements,
        ISqlService sql)
    {
        _profiles = profiles;
        _cells = cells;
        _info = info;
        _movements = movements;
        _sql = sql;
    }

    /// <summary>Item profile only. Reads issued invoices and on-hand, writes AbcSuggestion. Returns rows written.</summary>
    public async Task<int> BuildAsync(Guid profileId, DateTime asOf)
    {
        var profile = await LoadItemProfileAsync(profileId);
        var issued = await IssuedQtyAsync(profile, asOf);
        var onHand = await OnHandAsync();
        return await WriteAsync(profile, asOf, issued, onHand);
    }

    /// <summary>Same write path with caller-supplied demand (tests, no invoices).</summary>
    public async Task<int> BuildWithDemandAsync(
        Guid profileId,
        DateTime asOf,
        IReadOnlyDictionary<Guid, decimal> issued,
        IReadOnlyDictionary<Guid, decimal> onHand)
    {
        var profile = await LoadItemProfileAsync(profileId);
        return await WriteAsync(profile, asOf, issued, onHand);
    }

    private async Task<AbcProfile> LoadItemProfileAsync(Guid profileId)
    {
        var profile = await _profiles.GetRecordAsync(profileId)
            ?? throw new InvalidOperationException("Профиль ABC не найден");
        if (profile.IsDisabled)
            throw new InvalidOperationException("Профиль ABC отключён");
        if (!string.Equals(profile.Subject, "Item", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Пополнение считается только по номенклатуре");
        return profile;
    }

    private async Task<int> WriteAsync(
        AbcProfile profile,
        DateTime asOf,
        IReadOnlyDictionary<Guid, decimal> issued,
        IReadOnlyDictionary<Guid, decimal> onHand)
    {
        var cells = (await _cells.GetRecordsAsync("1 = 1"))
            .Where(c => c.Profile == profile.MetaId)
            .ToList();
        var slice = await _info.SliceLastAsync(
            "AbcClassification",
            asOf,
            new Dictionary<string, object?> { ["Profile"] = profile.MetaId });

        var from = asOf.Date.AddMonths(-profile.WindowMonths);
        var to = asOf.Date;
        var days = (decimal)(to - from).TotalDays;
        if (days < 1m) days = 1m;

        var keep = new HashSet<Guid>();
        var written = 0;
        foreach (var row in slice)
        {
            var subject = AsGuid(row, "Subject");
            if (subject == Guid.Empty) continue;
            var abc = Str(row, "AbcClass");
            var xyz = Str(row, "XyzClass");
            var cell = cells.FirstOrDefault(c =>
                string.Equals(Norm(c.AbcClass), Norm(abc), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Norm(c.XyzClass), Norm(xyz), StringComparison.OrdinalIgnoreCase));
            if (cell is null || cell.Mode == AbcStockMode.Unspecified) continue;

            issued.TryGetValue(subject, out var issuedQty);
            onHand.TryGetValue(subject, out var onHandQty);
            if (issuedQty < 0m) issuedQty = 0m;
            var suggest = cell.Mode == AbcStockMode.Order
                ? 0m
                : Math.Max(0m, cell.CoverDays * issuedQty / days - onHandQty);

            await _info.SetAsync(
                "AbcSuggestion",
                asOf,
                new Dictionary<string, object?>
                {
                    ["Profile"] = profile.MetaId,
                    ["Subject"] = subject,
                },
                new Dictionary<string, object?>
                {
                    ["AbcClass"] = Norm(abc),
                    ["XyzClass"] = Norm(xyz),
                    ["Mode"] = cell.Mode == AbcStockMode.Order ? "Order" : "Keep",
                    ["CoverDays"] = cell.CoverDays,
                    ["IssuedQty"] = issuedQty,
                    ["OnHandQty"] = onHandQty,
                    ["SuggestQty"] = suggest,
                });
            keep.Add(subject);
            written++;
        }

        await DropStaleAsync(profile.MetaId, asOf, keep);
        return written;
    }

    private async Task DropStaleAsync(Guid profileId, DateTime asOf, HashSet<Guid> keep)
    {
        var rows = await _info.QueryRecordsAsync("AbcSuggestion", asOf, asOf);
        foreach (var row in rows)
        {
            if (AsGuid(row, "Profile") != profileId) continue;
            var subject = AsGuid(row, "Subject");
            if (keep.Contains(subject)) continue;
            var id = AsGuid(row, "MetaId");
            if (id == Guid.Empty) continue;
            await _info.DeleteAsync("AbcSuggestion", id);
        }
    }

    private async Task<Dictionary<Guid, decimal>> IssuedQtyAsync(AbcProfile profile, DateTime asOf)
    {
        var from = asOf.Date.AddMonths(-profile.WindowMonths);
        var to = asOf.Date;
        var rows = await _sql.SelectAsync(
            "SELECT l.[Item] AS [Item], " +
            "SUM(CASE WHEN l.[BaseQuantity] <> 0 THEN l.[BaseQuantity] ELSE l.[Quantity] END) AS [Qty] " +
            "FROM [SalesRealization] h " +
            "INNER JOIN [TP_SalesInvoiceLines] l ON l.[OwnerMetaId] = h.[MetaId] " +
            "WHERE h.[Subtype] IN (N'Issued', N'Shipped') " +
            $"AND h.[DocumentDate] >= '{from:yyyy-MM-dd HH:mm:ss}' " +
            $"AND h.[DocumentDate] < '{to:yyyy-MM-dd HH:mm:ss}' " +
            "GROUP BY l.[Item]");
        var qty = new Dictionary<Guid, decimal>();
        foreach (var row in rows)
        {
            var item = AsGuid(row, "Item");
            if (item == Guid.Empty) continue;
            qty[item] = Decimal(row, "Qty");
        }
        return qty;
    }

    private async Task<Dictionary<Guid, decimal>> OnHandAsync()
    {
        var rows = await _movements.QueryBalancesAsync(StockRegister);
        var qty = new Dictionary<Guid, decimal>();
        foreach (var row in rows)
        {
            var item = AsGuid(row, "Item");
            if (item == Guid.Empty) continue;
            qty.TryGetValue(item, out var cur);
            qty[item] = cur + Decimal(row, "Qty");
        }
        return qty;
    }

    private static string Norm(string? value) => (value ?? string.Empty).Trim();

    private static string Str(IDictionary<string, object?> row, string column)
    {
        if (!row.TryGetValue(column, out var v) || v is null) return "";
        return v.ToString() ?? "";
    }

    private static Guid AsGuid(IDictionary<string, object?> row, string column)
    {
        if (!row.TryGetValue(column, out var v) || v is null) return Guid.Empty;
        return v is Guid g ? g : Guid.TryParse(v.ToString(), out var p) ? p : Guid.Empty;
    }

    private static decimal Decimal(IDictionary<string, object?> row, string column)
        => row.TryGetValue(column, out var v) && v != null
            ? Convert.ToDecimal(v, CultureInfo.InvariantCulture)
            : 0m;
}
