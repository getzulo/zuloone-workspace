using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;

// Unified warehouse-cell resolution (MIQS). The store is derived from the cell
// via the zone (StoreCell.StoreZone.Store), plus lookup of default
// receiving/storage/picking cells by cell type (StoreCellType.Name). Reused by
// Purchasing/Production/Sales postings and GL integration so cell ids are not
// hardcoded. StoreZone/Store/Type are required → on the entity they are Guid (not Guid?).
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

    // Saving a dictionary record from a service via inject drops a disposed IServiceProvider
    // (the handler re-resolves IStoreCellService). Same as PricingService.
    private static IDictionaryManager<StoreCell> LiveCells => ScriptServices.Get<IDictionaryManager<StoreCell>>();
    private static IDictionaryManager<StoreZone> LiveZones => ScriptServices.Get<IDictionaryManager<StoreZone>>();
    private static IDictionaryManager<StoreCellType> LiveTypes => ScriptServices.Get<IDictionaryManager<StoreCellType>>();
    private static IDictionaryManager<Store> LiveStores => ScriptServices.Get<IDictionaryManager<Store>>();

    /// <summary>
    /// Whether warehouse discipline is on: receipt only into receiving, shipment
    /// only from picking, tasks in between. OFF by default: turning it on
    /// at once would forbid everything that today puts goods into an arbitrary cell.
    /// </summary>
    public async Task<bool> IsWarehouseDisciplineOnAsync()
        => (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault()?.EnforceWarehouseTasks ?? false;

    /// <summary>
    /// Cell purpose — via its type. `Unspecified` means both "type without a
    /// purpose" and "no cell at all": for discipline that is the same answer
    /// "this cell does not fit the role", and there is no reason to split them.
    /// </summary>
    public async Task<StoreCellPurpose> GetCellPurposeAsync(Guid cell)
    {
        var c = await _cells.GetRecordAsync(cell);
        if (c is null) return StoreCellPurpose.Unspecified;
        var t = await _types.GetRecordAsync(c.Type);
        return t?.Purpose ?? StoreCellPurpose.Unspecified;
    }

    /// <summary>Whether the cell fits the role — honoring the flag. Discipline off
    /// → any cell fits: that is backward compatibility.</summary>
    public async Task<bool> IsCellAllowedForAsync(Guid cell, StoreCellPurpose purpose)
        => !await IsWarehouseDisciplineOnAsync() || await GetCellPurposeAsync(cell) == purpose;

    /// <summary>Store cell with a given PURPOSE (v1 — first match).
    /// Replaces lookup by type NAME: the name is free text, the role is a set
    /// in metadata.</summary>
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

    /// <summary>Where to put away received goods: this store's storage cell.</summary>
    public Task<Guid?> SuggestStorageCellAsync(Guid store) => GetCellByPurposeAsync(store, StoreCellPurpose.Storage);

    /// <summary>All cells of the store (via zones). Needed so free stock under
    /// discipline looks at the whole store: goods are still in storage while the
    /// order points at a picking cell.</summary>
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

    /// <summary>Cell's store: StoreCell → StoreZone → Store.</summary>
    public async Task<Guid?> GetStoreAsync(Guid cell)
    {
        var c = await _cells.GetRecordAsync(cell);
        if (c is null) return null;
        var z = await _zones.GetRecordAsync(c.StoreZone);
        return z?.Store;
    }

    /// <summary>
    /// Legal entity that owns the cell: StoreCell → StoreZone → Store →
    /// Division → LegalEntity. The accounting contour (taxes, GL) is kept by
    /// legal entity, while warehouse documents know only the cell — this chain
    /// is the bridge, so it lives here instead of being copied into handlers.
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

    /// <summary>First store cell whose type has the given name (Receiving/Storage/Picking).</summary>
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

    /// <summary>Suggested storage cell for the item (v1 — first Storage cell of the store).</summary>
    public Task<Guid?> SuggestPutAwayCellAsync(Guid store, Guid item) => GetDefaultCellByTypeAsync(store, "Storage");

    /// <summary>
    /// Finish the store's three-role cells if any are missing. Idempotent:
    /// receiving/storage/picking already present — creates nothing. A new store
    /// with discipline on and "turn the flag on" in settings call this so
    /// working data can be enabled without drawing cells by hand.
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

    /// <summary>Stamp Purpose on types named after a role and finish every
    /// store's yard. Returns how many cells were created.</summary>
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
