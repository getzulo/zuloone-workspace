using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Service "Bom": IBomService contract. Expands a product BOM into component
// demand — IN THE COMPONENT'S STOCK UNITS.
//
// The BOM is specified PER BATCH, not per unit: OutputQty is how many finished
// goods one recipe run yields. Demand = QtyPer × (order / OutputQty). A recipe
// "10 sandwiches from 20 g of sausage" on an order of 10 sandwiches needs 20 g,
// not 200: without dividing by OutputQty the field would be decorative, and the
// calculation would be wrong by exactly OutputQty.
//
// The BOM line has its own unit (sausage is specified in grams and stored in
// kilograms), so the result is converted to the item unit by the shared
// UnitConversionService — which also rounds to the target unit's precision.
//
// Data is read via typed IDictionaryManager<T>. A foreign service is taken
// through ScriptServices: model contracts live in the service registry, not in
// DI, so they cannot be constructor-injected. Inventory is a Production
// dependency, so the contract is compiled by the time this file compiles.
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

    public async Task<Dictionary<Guid, decimal>> ExpandByProductAsync(Guid product, decimal qty)
    {
        var result = new Dictionary<Guid, decimal>();

        var bom = (await _boms.GetRecordsAsync($"Product = '{product}'")).FirstOrDefault();
        if (bom == null) return result;

        // OutputQty ≤ 0 — a recipe with no stated yield: treat as "per unit",
        // otherwise a divide-by-zero would fail posting.
        var batches = bom.OutputQty > 0m ? qty / bom.OutputQty : qty;

        // Item converter, not the generic one: "2 boxes of a component" now means
        // a box of THIS component (one item has 12 pieces, another 6), whereas
        // the old global "box = 12" rule was wrong for every other item.
        var conversion = ScriptServices.Get<IItemQuantityConverter>();

        foreach (var comp in await _components.GetRecordsAsync($"Bom = '{bom.MetaId}'"))
        {
            var need = comp.QtyPer * batches;

            var item = await _items.GetRecordAsync(comp.Component);
            if (item != null && comp.Unit != Guid.Empty && comp.Unit != item.UnitOfMeasure)
            {
                // No conversion rule — silently treating grams as kilograms is not allowed.
                need = await conversion.ToBaseRoundedAsync(comp.Component, need, comp.Unit)
                    ?? throw new InvalidOperationException(
                        $"Нет правила перевода единиц для компонента спецификации «{bom.Name}»: "
                        + "количество задано в одной единице, а номенклатура хранится в другой. "
                        + "Заведите упаковку товара или коэффициент к базовой единице.");
            }

            result[comp.Component] = (result.TryGetValue(comp.Component, out var acc) ? acc : 0m) + need;
        }
        return result;
    }
}
