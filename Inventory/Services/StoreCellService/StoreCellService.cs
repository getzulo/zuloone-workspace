using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime;
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
    private readonly IDictionaryManager<Store> _stores;
    private readonly IDictionaryManager<Division> _divisions;
    private readonly IDictionaryManager<InventorySettings> _settings;

    public StoreCellService(
        IDictionaryManager<StoreCell> cells,
        IDictionaryManager<StoreZone> zones,
        IDictionaryManager<StoreCellType> types,
        IDictionaryManager<Store> stores,
        IDictionaryManager<Division> divisions,
        IDictionaryManager<InventorySettings> settings)
    {
        _cells = cells;
        _zones = zones;
        _types = types;
        _stores = stores;
        _divisions = divisions;
        _settings = settings;
    }

    // Запись справочника из сервиса через инжект роняет disposed IServiceProvider
    // (обработчик снова резолвит IStoreCellService). Как у PricingService.
    private static IDictionaryManager<StoreCell> LiveCells => ScriptServices.Get<IDictionaryManager<StoreCell>>();
    private static IDictionaryManager<StoreZone> LiveZones => ScriptServices.Get<IDictionaryManager<StoreZone>>();
    private static IDictionaryManager<StoreCellType> LiveTypes => ScriptServices.Get<IDictionaryManager<StoreCellType>>();
    private static IDictionaryManager<Store> LiveStores => ScriptServices.Get<IDictionaryManager<Store>>();

    /// <summary>
    /// Включена ли адресная дисциплина: приход только в приёмку, отгрузка только
    /// из отбора, между ними — задания. По умолчанию ВЫКЛЮЧЕНА: включение
    /// разом запретило бы всё, что сегодня кладёт товар в произвольную ячейку.
    /// </summary>
    public async Task<bool> IsWarehouseDisciplineOnAsync()
        => (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault()?.EnforceWarehouseTasks ?? false;

    /// <summary>
    /// Назначение ячейки — через её тип. `Unspecified` означает и «тип без
    /// назначения», и «ячейки нет вовсе»: для дисциплины это один и тот же ответ
    /// «в этой роли ячейка не годится», и разделять их незачем.
    /// </summary>
    public async Task<StoreCellPurpose> GetCellPurposeAsync(Guid cell)
    {
        var c = await _cells.GetRecordAsync(cell);
        if (c is null) return StoreCellPurpose.Unspecified;
        var t = await _types.GetRecordAsync(c.Type);
        return t?.Purpose ?? StoreCellPurpose.Unspecified;
    }

    /// <summary>Годится ли ячейка для роли — с учётом флага. Дисциплина выключена
    /// → годится любая: это и есть обратная совместимость.</summary>
    public async Task<bool> IsCellAllowedForAsync(Guid cell, StoreCellPurpose purpose)
        => !await IsWarehouseDisciplineOnAsync() || await GetCellPurposeAsync(cell) == purpose;

    /// <summary>Ячейка склада с заданным НАЗНАЧЕНИЕМ (v1 — первая подходящая).
    /// Пришла на смену подбору по ИМЕНИ типа: имя — свободный текст, роль — набор
    /// в метаданных.</summary>
    public async Task<Guid?> GetCellByPurposeAsync(Guid store, StoreCellPurpose purpose)
    {
        var typeIds = new HashSet<Guid>(
            (await _types.GetRecordsAsync("1 = 1")).Where(t => t.Purpose == purpose).Select(t => t.MetaId));
        if (typeIds.Count == 0) return null;

        var zoneIds = new HashSet<Guid>(
            (await _zones.GetRecordsAsync($"Store = '{store}'")).Select(z => z.MetaId));

        foreach (var c in await _cells.GetRecordsAsync("1 = 1"))
            if (typeIds.Contains(c.Type) && zoneIds.Contains(c.StoreZone))
                return c.MetaId;
        return null;
    }

    /// <summary>Куда раскладывать принятое: ячейка хранения этого склада.</summary>
    public Task<Guid?> SuggestStorageCellAsync(Guid store) => GetCellByPurposeAsync(store, StoreCellPurpose.Storage);

    /// <summary>Все ячейки склада (через зоны). Нужно, чтобы свободный остаток
    /// при дисциплине смотрел на склад целиком: товар ещё в хранении, а заказ
    /// указывает ячейку отбора.</summary>
    public async Task<List<Guid>> GetCellsOfStoreAsync(Guid store)
    {
        var zoneIds = new HashSet<Guid>(
            (await _zones.GetRecordsAsync($"Store = '{store}'")).Select(z => z.MetaId));
        var ids = new List<Guid>();
        foreach (var c in await _cells.GetRecordsAsync("1 = 1"))
            if (zoneIds.Contains(c.StoreZone))
                ids.Add(c.MetaId);
        return ids;
    }

    /// <summary>Склад ячейки: StoreCell → StoreZone → Store.</summary>
    public async Task<Guid?> GetStoreAsync(Guid cell)
    {
        var c = await _cells.GetRecordAsync(cell);
        if (c is null) return null;
        var z = await _zones.GetRecordAsync(c.StoreZone);
        return z?.Store;
    }

    /// <summary>
    /// Юрлицо, которому принадлежит ячейка: StoreCell → StoreZone → Store →
    /// Division → LegalEntity. Учётный контур (налоги, GL) ведётся по юрлицу, а
    /// документы складских операций знают только ячейку — эта цепочка и есть
    /// мост между ними, поэтому она живёт здесь, а не копируется в обработчики.
    /// </summary>
    public async Task<Guid?> GetLegalEntityAsync(Guid cell)
    {
        var store = await GetStoreAsync(cell);
        if (store is null) return null;
        var s = await _stores.GetRecordAsync(store.Value);
        if (s is null) return null;
        var d = await _divisions.GetRecordAsync(s.Division);
        return d?.LegalEntity;
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

    /// <summary>
    /// Дособрать складу ячейки трёх ролей, если какой-то нет. Идемпотентно:
    /// уже есть приёмка/хранение/отбор — ничего не плодит. Новый склад при
    /// включённой дисциплине и «включить флаг» на настройках зовут это, чтобы
    /// рабочие данные можно было включить, не рисуя ячейки руками.
    /// </summary>
    public async Task<int> EnsureYardAsync(Guid store)
    {
        if (store == Guid.Empty) return 0;
        if (await LiveStores.GetRecordAsync(store) is null) return 0;

        var zone = await EnsureZoneAsync(store);
        var created = 0;
        created += await EnsureRoleCellAsync(store, zone, StoreCellPurpose.Receiving, "RCV", "Receiving");
        created += await EnsureRoleCellAsync(store, zone, StoreCellPurpose.Storage, "STG", "Storage");
        created += await EnsureRoleCellAsync(store, zone, StoreCellPurpose.Picking, "PCK", "Picking");
        return created;
    }

    /// <summary>Проставить Purpose типам с именем роли и дособрать дворы всех
    /// складов. Возвращает, сколько ячеек создано.</summary>
    public async Task<int> PrepareAllYardsAsync()
    {
        await InferTypePurposesAsync();
        var created = 0;
        foreach (var store in await LiveStores.GetRecordsAsync("1 = 1"))
            created += await EnsureYardAsync(store.MetaId);
        return created;
    }

    private async Task InferTypePurposesAsync()
    {
        foreach (var type in await LiveTypes.GetRecordsAsync("1 = 1"))
        {
            if (type.Purpose != StoreCellPurpose.Unspecified) continue;
            var inferred = PurposeOfName(type.Name);
            if (inferred == StoreCellPurpose.Unspecified) continue;
            type.Purpose = inferred;
            await LiveTypes.SaveRecordAsync(type);
        }
    }

    private static StoreCellPurpose PurposeOfName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return StoreCellPurpose.Unspecified;
        if (name.Equals("Receiving", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Приёмка", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Приемка", StringComparison.OrdinalIgnoreCase))
            return StoreCellPurpose.Receiving;
        if (name.Equals("Storage", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Хранение", StringComparison.OrdinalIgnoreCase))
            return StoreCellPurpose.Storage;
        if (name.Equals("Picking", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Отбор", StringComparison.OrdinalIgnoreCase))
            return StoreCellPurpose.Picking;
        return StoreCellPurpose.Unspecified;
    }

    private async Task<Guid> EnsureZoneAsync(Guid store)
    {
        var existing = (await LiveZones.GetRecordsAsync($"Store = '{store}'")).FirstOrDefault();
        if (existing != null) return existing.MetaId;

        var zone = await LiveZones.NewRecordAsync();
        zone.Name = "Основная";
        zone.Store = store;
        zone.IsBarcodeTracking = false;
        return await LiveZones.SaveRecordAsync(zone);
    }

    private async Task<int> EnsureRoleCellAsync(
        Guid store, Guid zone, StoreCellPurpose purpose, string code, string name)
    {
        if (await GetCellByPurposeAsync(store, purpose) != null) return 0;

        var typeId = await EnsureTypeAsync(purpose, code, name);
        var next = 1;
        foreach (var c in await LiveCells.GetRecordsAsync($"StoreZone = '{zone}'"))
            if (c.CellNumber >= next) next = c.CellNumber + 1;

        var cell = await LiveCells.NewRecordAsync();
        cell.Name = $"{code}-01";
        cell.Type = typeId;
        cell.StoreZone = zone;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = next;
        await LiveCells.SaveRecordAsync(cell);
        return 1;
    }

    private async Task<Guid> EnsureTypeAsync(StoreCellPurpose purpose, string code, string name)
    {
        var all = await LiveTypes.GetRecordsAsync("1 = 1");
        var typed = all.FirstOrDefault(t => t.Purpose == purpose);
        if (typed != null) return typed.MetaId;

        var named = all.FirstOrDefault(t => PurposeOfName(t.Name) == purpose);
        if (named != null)
        {
            named.Purpose = purpose;
            await LiveTypes.SaveRecordAsync(named);
            return named.MetaId;
        }

        var taken = new HashSet<string>(all.Select(t => t.Code ?? ""), StringComparer.OrdinalIgnoreCase);
        var unique = code;
        var n = 1;
        while (taken.Contains(unique))
            unique = $"{code}{++n}";

        var created = await LiveTypes.NewRecordAsync();
        created.Code = unique.Length <= 16 ? unique : unique[..16];
        created.Name = name;
        created.Purpose = purpose;
        return await LiveTypes.SaveRecordAsync(created);
    }
}
