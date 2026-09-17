#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Thin Sales door: load contract overlays, ask IPricingService, write the line.
// ConvertPriceAsync is an intentional copy of PricingService — Inventory cannot
// take a public Convert and Sales cannot see a private helper.
public partial class SalesLinePricing
{
    public const string ManualMark = "Вручную";

    private readonly ILinkTableManager _rows;
    private readonly IDictionaryManager<SalesContract> _contracts;
    private readonly IDictionaryManager<Item> _items;

    public SalesLinePricing(
        ILinkTableManager rows,
        IDictionaryManager<SalesContract> contracts,
        IDictionaryManager<Item> items)
    {
        _rows = rows;
        _contracts = contracts;
        _items = items;
    }

    // Model services are not in the activator DI — only platform ones.
    private static IPricingService Pricing => ScriptServices.Get<IPricingService>();

    public bool IsManual(string? explanation, decimal unitPrice)
        => unitPrice > 0m
           && (string.Equals(explanation, ManualMark, StringComparison.Ordinal)
               || string.IsNullOrWhiteSpace(explanation));

    public async Task<Dictionary<string, object?>> ResolveForDocumentAsync(
        Guid item, Guid unit, decimal quantity, Guid customer, Guid contractId, DateTime onDate)
    {
        var priceType = Guid.Empty;
        var extras = new Dictionary<string, object?>();
        if (contractId != Guid.Empty)
        {
            var contract = await _contracts.GetRecordAsync(contractId);
            if (contract is not null)
            {
                priceType = contract.PriceType;
                var overlay = await OverrideOfAsync(contractId, item, unit, onDate.Date);
                if (overlay != null) extras["OverridePrice"] = overlay.Value;
                extras["QtyBreaks"] = await BreaksOfAsync(contractId);
            }
        }

        return await Pricing.ResolveSaleAsync(
            item, unit, customer == Guid.Empty ? null : customer, onDate,
            priceType, quantity, extras);
    }

    public async Task<Dictionary<string, object?>> ApplyFieldAsync(
        string fieldName,
        Guid item, Guid unit, decimal quantity, decimal unitPrice, string? explanation,
        Guid customer, Guid contractId, DateTime onDate)
    {
        var result = new Dictionary<string, object?>
        {
            ["Unit"] = unit,
            ["Quantity"] = quantity,
            ["UnitPrice"] = unitPrice,
            ["PriceExplanation"] = explanation ?? "",
        };

        if (item == Guid.Empty) return result;

        if (fieldName == "Item")
        {
            var card = await _items.GetRecordAsync(item);
            if (unit == Guid.Empty && card is not null && card.UnitOfMeasure != Guid.Empty)
            {
                unit = card.UnitOfMeasure;
                result["Unit"] = unit;
            }
            if (quantity <= 0m)
            {
                quantity = 1m;
                result["Quantity"] = quantity;
            }
        }

        if (fieldName == "UnitPrice")
        {
            result["PriceExplanation"] = ManualMark;
            return result;
        }

        if (unit == Guid.Empty) return result;
        if (IsManual(explanation, unitPrice)) return result;

        var resolved = await ResolveForDocumentAsync(item, unit, quantity, customer, contractId, onDate);
        if (resolved.TryGetValue("Price", out var p) && p != null)
        {
            result["UnitPrice"] = Convert.ToDecimal(p);
            result["PriceExplanation"] = resolved["Explanation"] as string ?? "";
        }
        else
        {
            result["PriceExplanation"] = resolved["Explanation"] as string ?? "Цена не задана";
        }
        return result;
    }

    private async Task<decimal?> OverrideOfAsync(Guid contractId, Guid item, Guid unit, DateTime day)
    {
        var rows = (await _rows.GetRecordsAsync<LT_SalesContractPrice>(
                new Dictionary<string, object?> { ["SalesContract"] = contractId, ["Item"] = item }))
            .Where(r => Covers(r.EffectiveFrom, r.EffectiveTo, day))
            .ToList();
        if (rows.Count == 0) return null;

        var exact = rows.FirstOrDefault(r => r.Unit == unit);
        if (exact?.Price is decimal exactPrice) return exactPrice;

        decimal? found = null;
        foreach (var row in rows)
        {
            if (row.Price is not decimal price || row.Unit is not Guid fromUnit) continue;
            var converted = await ConvertPriceAsync(item, price, fromUnit, unit);
            if (converted == null) continue;
            if (found != null && found.Value != converted.Value) return null;
            found ??= converted;
        }
        return found;
    }

    private async Task<List<Dictionary<string, object?>>> BreaksOfAsync(Guid contractId)
    {
        var rows = await _rows.GetRecordsAsync<LT_SalesContractQtyBreak>(
            new Dictionary<string, object?> { ["SalesContract"] = contractId });
        return rows.Select(r => new Dictionary<string, object?>
        {
            ["Item"] = r.Item ?? Guid.Empty,
            ["MinQty"] = r.MinQty ?? 0m,
            ["DiscountPercent"] = r.DiscountPercent ?? 0m,
        }).ToList();
    }

    private static bool Covers(DateTime? from, DateTime? to, DateTime day)
        => day.Date >= (from?.Date ?? DateTime.MinValue.Date)
        && day.Date <= (to?.Date ?? DateTime.MaxValue.Date);

    // Copy of PricingService.ConvertPriceAsync — see class comment.
    private static async Task<decimal?> ConvertPriceAsync(Guid item, decimal price, Guid fromUnit, Guid toUnit)
    {
        if (fromUnit == toUnit) return price;
        var conversion = ScriptServices.Get<IItemQuantityConverter>();
        var oneTarget = await conversion.ToBaseAsync(item, 1m, toUnit);
        var oneSource = await conversion.ToBaseAsync(item, 1m, fromUnit);
        if (oneTarget == null || oneSource == null || oneSource.Value == 0m) return null;
        var amount = price * oneTarget.Value / oneSource.Value;
        return Math.Round(amount, GlobalConstants.Get<int?>("AmountScale") ?? 2, MidpointRounding.AwayFromZero);
    }
}
