using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;

// The only door into prices. Invoice, order, the "fill" command, and dictionary
// handlers do not read rows themselves and do not compare windows — they ask here.
//
// Four verbs on the outside: resolve, compute amount, set, capture history.
// Capture is an explicit call (type "from last document"), not a side effect
// of posting. Auto-writing the customer's type from every invoice would overwrite
// a shared retail price with someone else's contract price.
public partial class PricingService
{
    private static int MaxPriceTypeChainDepth => GlobalConstants.Get<int?>("PriceTypeChainMaxDepth") ?? 20;

    private readonly IDictionaryManager<PriceType> _types;
    private readonly ILinkTableManager _rows;
    private readonly IDictionaryManager<Item> _items;

    public PricingService(
        IDictionaryManager<PriceType> types,
        ILinkTableManager rows,
        IDictionaryManager<Item> items)
    {
        _types = types;
        _rows = rows;
        _items = items;
    }

    private Task<List<LT_PriceTypeHistory>> RowsOfAsync(Guid priceType, Guid item, Guid? unit = null)
    {
        var match = new Dictionary<string, object?> { ["PriceType"] = priceType, ["Item"] = item };
        if (unit != null) match["Unit"] = unit.Value;
        return _rows.GetRecordsAsync<LT_PriceTypeHistory>(match);
    }

    public decimal LineAmount(decimal quantity, decimal unitPrice)
        => LineAmount(quantity, unitPrice, 0m);

    /// <summary>Discount is a percent (15 = 15%), not a fraction. Every money
    /// leg of the document must pass the same one.</summary>
    public decimal LineAmount(decimal quantity, decimal unitPrice, decimal discountPercent)
    {
        var gross = quantity * unitPrice;
        var net = discountPercent > 0m ? gross * (1m - discountPercent / 100m) : gross;
        return Math.Round(net, GlobalConstants.Get<int?>("AmountScale") ?? 2, MidpointRounding.AwayFromZero);
    }

    public async Task<decimal?> ResolveSalePriceAsync(Guid item, Guid unit, Guid? customer, DateTime onDate)
        => await ResolveAsync(item, unit, await PriceTypeOfAsync(customer, sale: true), onDate, sale: true);

    public async Task<decimal?> ResolvePurchasePriceAsync(Guid item, Guid unit, Guid? supplier, DateTime onDate)
        => await ResolveAsync(item, unit, await PriceTypeOfAsync(supplier, sale: false), onDate, sale: false);

    /// <summary>Price of exactly this type, no item-card default. No price — null.</summary>
    public Task<decimal?> ResolveForTypeAsync(Guid item, Guid unit, Guid priceType, DateTime onDate)
        => ResolvePriceForTypeAsync(item, unit, priceType, onDate.Date, depth: 0);

    /// <summary>Manual/import. The same checks as when saving a row.</summary>
    public async Task<Guid> SetPriceAsync(
        Guid priceType, Guid item, Guid unit, decimal price, DateTime? from, DateTime? to)
    {
        var row = new LT_PriceTypeHistory
        {
            PriceType = priceType,
            Item = item,
            Unit = unit,
            Price = price,
            EffectiveFrom = from,
            EffectiveTo = to,
        };
        var error = await ValidateRowAsync(Guid.Empty, priceType, item, unit, price, from, to);
        if (error != null) throw new InvalidOperationException(error);

        // Write via ScriptServices, not the injected manager:
        // Save from an inject inside the service drops a disposed IServiceProvider on
        // GetEventHandler (the handler re-resolves IPricingService).
        return await ScriptServices.Get<ILinkTableManager>().SaveRecordAsync(row);
    }

    public Task CaptureSalePriceAsync(Guid item, Guid unit, Guid? customer, decimal price, DateTime onDate)
        => CapturePriceAsync(item, unit, customer, sale: true, price, onDate);

    public Task CapturePurchasePriceAsync(Guid item, Guid unit, Guid? supplier, decimal price, DateTime onDate)
        => CapturePriceAsync(item, unit, supplier, sale: false, price, onDate);

    /// <summary>Window-overlap predicate for one triple. Null — no overlap.
    /// The entity is not on the contract: the IPricingService assembly and the script see different types.</summary>
    public async Task<Guid?> FindOverlappingAsync(
        Guid priceType, Guid item, Guid unit, Guid excludeRow, DateTime? from, DateTime? to)
    {
        var siblings = await RowsOfAsync(priceType, item, unit);
        var clash = siblings.FirstOrDefault(other =>
            other.MetaId != excludeRow && WindowsOverlap(from, to, other.EffectiveFrom, other.EffectiveTo));
        return clash?.MetaId;
    }

    /// <summary>Type consistency. kind: 0 = Base, 1 = Calculated. Null — valid.</summary>
    public async Task<string?> ValidateTypeAsync(Guid metaId, int kind, Guid basePriceType, decimal markupPercent)
    {
        if (kind == (int)PriceTypeKind.Base)
        {
            if (basePriceType != Guid.Empty || markupPercent != 0)
                return "Базовый тип цены не может ссылаться на другой тип цены и не может иметь наценку — заполни цены строками";
            return null;
        }

        if (basePriceType == Guid.Empty)
            return "Динамический тип цены обязан ссылаться на базовый тип цены";

        if (markupPercent <= -100m)
            return "Наценка не может быть -100% или меньше — цена базового типа обнулится или уйдёт в минус";

        var existingRows = await _rows.GetRecordsAsync<LT_PriceTypeHistory>(
            new Dictionary<string, object?> { ["PriceType"] = metaId });
        if (existingRows.Any())
            return "У этого типа цены уже есть строки — удали их перед переключением в Calculated";

        var visited = new HashSet<Guid> { metaId };
        var currentId = basePriceType;
        var depth = 0;
        while (true)
        {
            if (!visited.Add(currentId))
                return "Цепочка базовых типов цены зациклилась";
            if (++depth > MaxPriceTypeChainDepth)
                return $"Цепочка базовых типов цены длиннее {MaxPriceTypeChainDepth} уровней";

            var current = await _types.GetRecordAsync(currentId);
            if (current == null || current.Kind == PriceTypeKind.Base || current.BasePriceType == Guid.Empty)
                break;
            currentId = current.BasePriceType;
        }

        return null;
    }

    /// <summary>Price row: sign, Base type, window, item unit, overlap.</summary>
    public async Task<string?> ValidateRowAsync(
        Guid metaId, Guid priceTypeId, Guid itemId, Guid unit, decimal price, DateTime? from, DateTime? to)
    {
        if (price <= 0m)
            return "Цена должна быть больше нуля";

        var priceType = await _types.GetRecordAsync(priceTypeId);
        if (priceType == null)
            return "Тип цены не найден";

        if (priceType.Kind == PriceTypeKind.Calculated)
            return $"Тип цены «{priceType.Name}» — расчётный: цена вычисляется от базового типа, а не задаётся строками";

        var fromDay = from ?? DateTime.MinValue;
        var toDay = to ?? DateTime.MaxValue;
        if (fromDay.Date > toDay.Date)
            return "Дата начала действия цены позже даты окончания";

        var item = await _items.GetRecordAsync(itemId);
        if (item == null)
            return "Товар не найден";

        if (unit != item.UnitOfMeasure)
        {
            var factor = await ScriptServices.Get<IUnitConverter>().FactorAsync(unit, item.UnitOfMeasure);
            var packaging = (await ScriptServices.Get<IDictionaryManager<ItemUnit>>()
                    .GetRecordsAsync($"Item = '{itemId}' AND Unit = '{unit}'"))
                .Any();
            if (factor == null && !packaging)
                return "Цену нельзя задать в этой единице: она не базовая для товара, не заведена его упаковкой и не приводится к базовой по виду величины";
        }

        var clash = await FindOverlappingAsync(priceTypeId, itemId, unit, metaId, from, to);
        if (clash != null)
            return "Для этого товара в этом типе цен и этой единице уже есть цена на пересекающийся период";

        return null;
    }

    /// <summary>
    /// History from an actual deal price: close the covering row a day
    /// earlier, open a new one. Base only. Past rows with EffectiveTo filled
    /// are left alone. The document does not call this itself — only whoever
    /// deliberately keeps a type "from last document".
    /// </summary>
    private async Task CapturePriceAsync(Guid item, Guid unit, Guid? party, bool sale, decimal price, DateTime onDate)
    {
        if (item == Guid.Empty || unit == Guid.Empty || price <= 0m) return;

        var priceTypeId = await PriceTypeOfAsync(party, sale);
        if (priceTypeId is not Guid typeId) return;

        var priceType = await _types.GetRecordAsync(typeId);
        if (priceType == null || priceType.Kind != PriceTypeKind.Base) return;

        var rows = await RowsOfAsync(typeId, item, unit);
        var onDateOnly = onDate.Date;
        var covering = rows.FirstOrDefault(r => Covers(r, onDateOnly));

        if (covering != null)
        {
            if (covering.Price == price) return;
            if (covering.EffectiveTo != null) return;

            if ((covering.EffectiveFrom ?? DateTime.MinValue).Date == onDateOnly)
            {
                covering.Price = price;
                await ScriptServices.Get<ILinkTableManager>().SaveRecordAsync(covering);
                return;
            }

            covering.EffectiveTo = onDateOnly.AddDays(-1);
            await ScriptServices.Get<ILinkTableManager>().SaveRecordAsync(covering);
        }

        var next = rows.Where(r => r.EffectiveFrom > onDateOnly).OrderBy(r => r.EffectiveFrom).FirstOrDefault();
        await ScriptServices.Get<ILinkTableManager>().SaveRecordAsync(new LT_PriceTypeHistory
        {
            PriceType = typeId,
            Item = item,
            Unit = unit,
            Price = price,
            EffectiveFrom = onDateOnly,
            EffectiveTo = next?.EffectiveFrom?.Date.AddDays(-1),
        });
    }

    private async Task<Guid?> PriceTypeOfAsync(Guid? party, bool sale)
    {
        if (party == null || party == Guid.Empty) return null;

        Guid typeId;
        if (sale)
        {
            var customer = await ScriptServices.Get<IDictionaryManager<Customer>>().GetRecordAsync(party.Value);
            typeId = customer?.PriceType ?? Guid.Empty;
        }
        else
        {
            var supplier = await ScriptServices.Get<IDictionaryManager<Supplier>>().GetRecordAsync(party.Value);
            typeId = supplier?.PriceType ?? Guid.Empty;
        }
        if (typeId == Guid.Empty) return null;

        var type = await _types.GetRecordAsync(typeId);
        if (type == null || type.IsDisabled) return null;
        var wanted = sale ? PriceDirection.Sale : PriceDirection.Purchase;
        return type.Direction == wanted ? typeId : (Guid?)null;
    }

    private async Task<decimal?> ResolveAsync(Guid item, Guid unit, Guid? priceType, DateTime onDate, bool sale)
    {
        if (priceType is Guid typeId)
        {
            var resolved = await ResolvePriceForTypeAsync(item, unit, typeId, onDate, depth: 0);
            if (resolved != null) return resolved;
        }

        var card = await _items.GetRecordAsync(item);
        if (card == null) return null;

        var fallback = sale ? card.DefaultSalePrice : card.DefaultPurchasePrice;
        if (fallback <= 0m) return null;

        return unit == card.UnitOfMeasure
            ? fallback
            : await ConvertPriceAsync(item, fallback, card.UnitOfMeasure, unit);
    }

    private async Task<decimal?> ResolvePriceForTypeAsync(Guid item, Guid unit, Guid priceTypeId, DateTime onDate, int depth)
    {
        if (depth > MaxPriceTypeChainDepth) return null;

        var priceType = await _types.GetRecordAsync(priceTypeId);
        if (priceType == null || priceType.IsDisabled) return null;

        if (priceType.Kind == PriceTypeKind.Base)
        {
            var rows = (await RowsOfAsync(priceTypeId, item))
                .Where(r => Covers(r, onDate))
                .ToList();

            var exact = rows.FirstOrDefault(r => r.Unit == unit);
            if (exact != null) return exact.Price;

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

        if (priceType.BasePriceType == Guid.Empty) return null;

        var basePrice = await ResolvePriceForTypeAsync(item, unit, priceType.BasePriceType, onDate, depth + 1);
        if (basePrice == null) return null;

        var marked = basePrice.Value * (1m + priceType.MarkupPercent / 100m);
        var rounded = Math.Round(marked, GlobalConstants.Get<int?>("AmountScale") ?? 2, MidpointRounding.AwayFromZero);
        return rounded > 0m ? rounded : (decimal?)null;
    }

    private static bool Covers(LT_PriceTypeHistory row, DateTime onDate)
    {
        var day = onDate.Date;
        return day >= (row.EffectiveFrom?.Date ?? DateTime.MinValue.Date)
            && day <= (row.EffectiveTo?.Date ?? DateTime.MaxValue.Date);
    }

    private static bool WindowsOverlap(DateTime? aFrom, DateTime? aTo, DateTime? bFrom, DateTime? bTo)
        => (aFrom?.Date ?? DateTime.MinValue.Date) <= (bTo?.Date ?? DateTime.MaxValue.Date)
        && (bFrom?.Date ?? DateTime.MinValue.Date) <= (aTo?.Date ?? DateTime.MaxValue.Date);

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
