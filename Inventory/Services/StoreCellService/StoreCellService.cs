using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Generated;

// Единый резолвинг ячеек склада (MIQS). Склад выводится из ячейки через зону
// (StoreCell.StoreZone.Store), плюс поиск дефолтных ячеек приёмки/хранения/отбора
// по типу ячейки (StoreCellType.Name). Переиспользуется проводками Purchasing/
// Production/Sales и GL-интеграцией, чтобы не хардкодить id ячеек.
// Поля StoreZone/Store/Type обязательные → у сущности это Guid (не Guid?).
public partial class StoreCellService
{
    private readonly IDictionaryManager<StoreCell> _cells;
    private readonly IDictionaryManager<StoreZone> _zones;
    private readonly IDictionaryManager<StoreCellType> _types;

    public StoreCellService(
        IDictionaryManager<StoreCell> cells,
        IDictionaryManager<StoreZone> zones,
        IDictionaryManager<StoreCellType> types)
    {
        _cells = cells;
        _zones = zones;
        _types = types;
    }

    /// <summary>Склад ячейки: StoreCell → StoreZone → Store.</summary>
    public async Task<Guid?> GetStoreAsync(Guid cell)
    {
        var c = await _cells.GetRecordAsync(cell);
        if (c is null) return null;
        var z = await _zones.GetRecordAsync(c.StoreZone);
        return z?.Store;
    }

    /// <summary>Первая ячейка склада с типом заданного имени (Receiving/Storage/Picking).</summary>
    public async Task<Guid?> GetDefaultCellByTypeAsync(Guid store, string typeName)
    {
        var type = (await _types.GetRecordsAsync($"Name = '{typeName}'")).FirstOrDefault();
        if (type is null) return null;

        var zoneIds = new HashSet<Guid>(
            (await _zones.GetRecordsAsync($"Store = '{store}'")).Select(z => z.MetaId));

        foreach (var c in await _cells.GetRecordsAsync($"Type = '{type.MetaId}'"))
            if (zoneIds.Contains(c.StoreZone))
                return c.MetaId;
        return null;
    }

    public Task<Guid?> GetDefaultReceivingCellAsync(Guid store) => GetDefaultCellByTypeAsync(store, "Receiving");
    public Task<Guid?> GetDefaultPickingCellAsync(Guid store) => GetDefaultCellByTypeAsync(store, "Picking");
    public Task<Guid?> GetDefaultOutputCellAsync(Guid store) => GetDefaultCellByTypeAsync(store, "Picking");

    /// <summary>Рекомендуемая ячейка хранения под товар (v1 — первая Storage-ячейка склада).</summary>
    public Task<Guid?> SuggestPutAwayCellAsync(Guid store, Guid item) => GetDefaultCellByTypeAsync(store, "Storage");
}
