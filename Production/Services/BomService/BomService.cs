using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Service "Bom": IBomService contract. Expands a product BOM into component
// demand — IN THE COMPONENT'S STOCK UNITS.
//
// The BOM is specified PER BATCH, not per unit: OutputQty is how many finished
// goods one recipe run yields. Demand = QtyPer × (order / OutputQty).
//
// A line with Explode = false (the default) is demanded as stock. Explode =
// true walks that component's own BOM and merges the leaves. A cycle throws.
public partial class BomService
{
    private readonly IDictionaryManager<BillOfMaterials> _boms;
    private readonly IDictionaryManager<BomComponent> _components;
    private readonly IDictionaryManager<Item> _items;

    public BomService(
        IDictionaryManager<BillOfMaterials> boms,
        IDictionaryManager<BomComponent> components,
        IDictionaryManager<Item> items)
    {
        _boms = boms;
        _components = components;
        _items = items;
    }

    public Task<Dictionary<Guid, decimal>> ExpandByProductAsync(Guid product, decimal qty)
        => ExpandAsync(product, qty, new HashSet<Guid>());

    private async Task<Dictionary<Guid, decimal>> ExpandAsync(Guid product, decimal qty, HashSet<Guid> trail)
    {
        var result = new Dictionary<Guid, decimal>();
        if (product == Guid.Empty) return result;

        if (!trail.Add(product))
            throw new InvalidOperationException(
                "Цикл в спецификации: изделие ссылается само на себя через разворачиваемые компоненты.");

        try
        {
            var bom = (await _boms.GetRecordsAsync($"Product = '{product}'")).FirstOrDefault();
            if (bom == null) return result;

            var batches = bom.OutputQty > 0m ? qty / bom.OutputQty : qty;
            var conversion = ScriptServices.Get<IItemQuantityConverter>();

            foreach (var comp in await _components.GetRecordsAsync($"Bom = '{bom.MetaId}'"))
            {
                var need = await NeedInStockUnitAsync(bom.Name, comp, batches, conversion);
                if (comp.Explode)
                {
                    var nested = await ExpandAsync(comp.Component, need, trail);
                    if (nested.Count == 0)
                        Add(result, comp.Component, need);
                    else
                        foreach (var kv in nested)
                            Add(result, kv.Key, kv.Value);
                }
                else
                {
                    Add(result, comp.Component, need);
                }
            }
        }
        finally
        {
            trail.Remove(product);
        }

        return result;
    }

    private async Task<decimal> NeedInStockUnitAsync(
        string bomName, BomComponent comp, decimal batches, IItemQuantityConverter conversion)
    {
        var need = comp.QtyPer * batches;
        var item = await _items.GetRecordAsync(comp.Component);
        if (item != null && comp.Unit != Guid.Empty && comp.Unit != item.UnitOfMeasure)
        {
            need = await conversion.ToBaseRoundedAsync(comp.Component, need, comp.Unit)
                ?? throw new InvalidOperationException(
                    $"Нет правила перевода единиц для компонента спецификации «{bomName}»: "
                    + "количество задано в одной единице, а номенклатура хранится в другой. "
                    + "Заведите упаковку товара или коэффициент к базовой единице.");
        }
        return need;
    }

    private static void Add(Dictionary<Guid, decimal> result, Guid item, decimal qty)
        => result[item] = (result.TryGetValue(item, out var acc) ? acc : 0m) + qty;
}
