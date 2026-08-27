using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Generated;

// Сервис "Bom": контракт IBomService. Разворачивает спецификацию изделия в
// потребность компонентов = ΣQtyPer × количество заказа (по первой найденной
// спецификации на изделие; одинаковые компоненты суммируются).
//
// Данные читаются через ТИПИЗИРОВАННЫЙ IDictionaryManager<T> — это работа с
// сущностями (bom.MetaId, comp.Component, comp.QtyPer), а не с сырыми словарями
// IDataService и ручными кастами (Guid)row["Component"]. Для чтения записей
// справочника это правильный высокоуровневый сервис.
public partial class BomService
{
    private readonly IDictionaryManager<BillOfMaterials> _boms;
    private readonly IDictionaryManager<BomComponent> _components;

    public BomService(IDictionaryManager<BillOfMaterials> boms, IDictionaryManager<BomComponent> components)
    {
        _boms = boms;
        _components = components;
    }

    public async Task<Dictionary<Guid, decimal>> ExpandByProductAsync(Guid product, decimal qty)
    {
        var result = new Dictionary<Guid, decimal>();

        var bom = (await _boms.GetRecordsAsync($"Product = '{product}'")).FirstOrDefault();
        if (bom == null) return result;

        foreach (var comp in await _components.GetRecordsAsync($"Bom = '{bom.MetaId}'"))
        {
            result[comp.Component] = (result.TryGetValue(comp.Component, out var acc) ? acc : 0m)
                + comp.QtyPer * qty;
        }
        return result;
    }
}
