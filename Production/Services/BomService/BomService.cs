using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Сервис "Bom": контракт IBomService. Разворачивает спецификацию изделия в
// потребность компонентов — В СКЛАДСКИХ ЕДИНИЦАХ компонента.
//
// Спецификация задаётся НА ПАРТИЮ, а не на единицу: OutputQty — сколько изделий
// даёт один прогон рецепта. Потребность = QtyPer × (заказ / OutputQty). Рецепт
// «10 бутербродов из 20 г колбасы» на заказ в 10 бутербродов требует 20 г, а не
// 200: без деления на OutputQty поле было бы декоративным, а расчёт врал бы ровно
// в OutputQty раз.
//
// Единица строки спецификации своя (колбаса нормируется в граммах, а хранится в
// килограммах), поэтому итог переводится в единицу номенклатуры общим
// UnitConversionService — он же округляет по точности целевой единицы.
//
// Данные читаются типизированным IDictionaryManager<T>. Чужой сервис берётся
// через ScriptServices: контракты моделей живут в реестре сервисов, а не в DI,
// поэтому конструктором его не внедрить. Inventory лежит в зависимостях
// Production, значит контракт собран к моменту компиляции этого файла.
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

        // OutputQty ≤ 0 — рецепт без указанного выхода: считаем «на единицу»,
        // иначе деление на ноль уронило бы проведение.
        var batches = bom.OutputQty > 0m ? qty / bom.OutputQty : qty;

        var conversion = ScriptServices.Get<IUnitConversionService>();

        foreach (var comp in await _components.GetRecordsAsync($"Bom = '{bom.MetaId}'"))
        {
            var need = comp.QtyPer * batches;

            var item = await _items.GetRecordAsync(comp.Component);
            if (item != null && comp.Unit != Guid.Empty && comp.Unit != item.UnitOfMeasure)
            {
                // Правила перевода нет — молча считать граммы килограммами нельзя.
                need = await conversion.ConvertRoundedAsync(need, comp.Unit, item.UnitOfMeasure)
                    ?? throw new InvalidOperationException(
                        $"Нет правила перевода единиц для компонента спецификации «{bom.Name}»: "
                        + "количество задано в одной единице, а номенклатура хранится в другой.");
            }

            result[comp.Component] = (result.TryGetValue(comp.Component, out var acc) ? acc : 0m) + need;
        }
        return result;
    }
}
