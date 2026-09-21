#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;

// Gross margin of recognized sales. CostingIssue writes InventoryValue
// Value negative on the same document that posted Revenue. Scrap and
// production issues have no Revenue row, so they stay out of this number.
// Labor share of Payroll is a different leftover (HR).
public partial class GrossMarginService
{
    private static readonly Guid RevenueRegister = Guid.Parse("103cdb23-2d49-4c42-9bf3-3c5a8ce46cdc");
    private static readonly Guid InventoryValueRegister = Guid.Parse("7e0b4d85-1a6f-4c9d-8e5b-8f1a4c7d0e60");

    private readonly IRegisterMovementService _movements;
    private readonly AnalyticSetService _analytics;

    public GrossMarginService(IRegisterMovementService movements, AnalyticSetService analytics)
    {
        _movements = movements;
        _analytics = analytics;
    }

    /// <summary>Sum of Revenue.Amount for the item in [from, to).</summary>
    public async Task<decimal> RevenueOfAsync(Guid item, DateTime from, DateTime to)
        => await SumAsync(RevenueRegister, "Amount", item, from, to, saleDocsOnly: false, negativesOnly: false);

    /// <summary>Abs of InventoryValue.Value &lt; 0 on documents that also posted Revenue.</summary>
    public async Task<decimal> CogsOfAsync(Guid item, DateTime from, DateTime to)
        => await SumAsync(InventoryValueRegister, "Value", item, from, to, saleDocsOnly: true, negativesOnly: true);

    /// <summary>Revenue minus COGS of those sales.</summary>
    public async Task<decimal> MarginOfAsync(Guid item, DateTime from, DateTime to)
    {
        var revenue = await RevenueOfAsync(item, from, to);
        var cogs = await CogsOfAsync(item, from, to);
        return revenue - cogs;
    }

    private async Task<decimal> SumAsync(
        Guid register,
        string resource,
        Guid item,
        DateTime from,
        DateTime to,
        bool saleDocsOnly,
        bool negativesOnly)
    {
        if (item == Guid.Empty) return 0m;
        var filter =
            $"[MovementDate] >= '{from:yyyy-MM-dd HH:mm:ss}' AND [MovementDate] < '{to:yyyy-MM-dd HH:mm:ss}'";
        HashSet<Guid>? saleDocs = null;
        if (saleDocsOnly)
        {
            var revenue = await _movements.QueryMovementsAsync(RevenueRegister, filter);
            saleDocs = revenue
                .Select(m => AsGuid(m, "DocumentMetaId"))
                .Where(id => id != Guid.Empty)
                .ToHashSet();
            if (saleDocs.Count == 0) return 0m;
        }

        var rows = await _movements.QueryMovementsAsync(register, filter);
        if (rows.Count == 0) return 0m;
        var setIds = rows.Select(m => AsGuid(m, "AnalyticSetMetaId"))
            .Where(id => id != Guid.Empty).Distinct().ToList();
        var sets = await _analytics.ExpandAsync(setIds);
        var total = 0m;
        foreach (var row in rows)
        {
            if (saleDocs is not null)
            {
                var doc = AsGuid(row, "DocumentMetaId");
                if (doc == Guid.Empty || !saleDocs.Contains(doc)) continue;
            }
            if (ItemOf(row, sets) != item) continue;
            var amount = Decimal(row, resource);
            if (negativesOnly)
            {
                if (amount >= 0m) continue;
                total += -amount;
            }
            else
            {
                total += amount;
            }
        }
        return total;
    }

    private static Guid ItemOf(
        Dictionary<string, object?> row,
        Dictionary<Guid, Dictionary<string, string>> sets)
    {
        var direct = AsGuid(row, "Item");
        if (direct != Guid.Empty) return direct;
        var setId = AsGuid(row, "AnalyticSetMetaId");
        if (setId == Guid.Empty || !sets.TryGetValue(setId, out var values)) return Guid.Empty;
        return values.TryGetValue("Item", out var v) && Guid.TryParse(v, out var g) ? g : Guid.Empty;
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
