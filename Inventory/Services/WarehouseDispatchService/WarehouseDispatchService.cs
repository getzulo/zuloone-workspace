using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Диспетчерская склада: server-side агрегатор для дашборда «пульт диспетчера».
// Одним вызовом GetDispatchBoard собирает: KPI (приход/расход за смену, открытые
// задачи, занятость, средняя загрузка), зоны (занятость/вместимость/%/оборот),
// ячейки (адрес + занятость + MaxQty + % загрузки → для карты-хитмапа) и ленту
// последних движений. Читает регистр Stock (баланс TB / движения TR) через
// IRegisterMovementService, ячейки/зоны/товары — через IDictionaryManager, бэклог —
// счётом документов PutAwayTask/PickTask в подтипе Draft. External-бакет двойной
// записи из занятости/ленты исключаем. Всё это только чтение — фронт рисует.
public partial class WarehouseDispatchService
{
    private static readonly Guid External = Guid.Parse("e0000000-0000-4000-8000-0000000000e1");

    private readonly ITotalsManager _totals;
    private readonly IDictionaryManager<StoreCell> _cells;
    private readonly IDictionaryManager<StoreZone> _zones;
    private readonly IDictionaryManager<Item> _items;
    private readonly IDataService _data;

    public WarehouseDispatchService(
        ITotalsManager totals,
        IDictionaryManager<StoreCell> cells,
        IDictionaryManager<StoreZone> zones,
        IDictionaryManager<Item> items,
        IDataService data)
    {
        _totals = totals;
        _cells = cells;
        _zones = zones;
        _items = items;
        _data = data;
    }

    /// <summary>Единый снимок склада для дашборда. shiftHours — окно «смены» (по умолчанию 12ч).</summary>
    public async Task<object> GetDispatchBoard(Guid? store = null, int shiftHours = 12)
    {
        var cells = await _cells.GetRecordsAsync();
        var zones = (await _zones.GetRecordsAsync()).ToDictionary(z => z.MetaId);
        var itemNames = (await _items.GetRecordsAsync()).ToDictionary(i => i.MetaId, i => i.Name);

        // Занятость по ячейке = сумма Qty баланса (External исключаем).
        var occByCell = new Dictionary<Guid, decimal>();
        foreach (var b in await _totals.QueryBalancesAsync("Stock"))
        {
            var cellId = AsGuid(b, "Cell");
            if (cellId == External || cellId == Guid.Empty) continue;
            occByCell[cellId] = Get(occByCell, cellId) + AsDec(b, "Qty");
        }

        // Движения: лента (последние 40) + оборот/приход/расход за смену.
        var shiftStart = DateTime.UtcNow.AddHours(-Math.Abs(shiftHours));
        decimal inQty = 0m, outQty = 0m;
        var thrByCell = new Dictionary<Guid, decimal>();
        var recent = new List<object>();
        foreach (var m in await _totals.QueryMovementsAsync("Stock", orderBy: "[MovementDate] DESC", take: 300))
        {
            var cellId = AsGuid(m, "Cell");
            if (cellId == External) continue;
            var qty = AsDec(m, "Qty");
            var date = AsDate(m, "MovementDate");
            if (date >= shiftStart)
            {
                if (qty >= 0m) inQty += qty; else outQty += -qty;
                thrByCell[cellId] = Get(thrByCell, cellId) + Math.Abs(qty);
            }
            if (recent.Count < 40)
                recent.Add(new
                {
                    date,
                    cell = CellName(cellId, cells),
                    item = itemNames.TryGetValue(AsGuid(m, "Item"), out var nm) ? nm : "",
                    qty,
                    dir = qty >= 0m ? "in" : "out"
                });
        }

        // Ячейки (для хитмапа) + свёртка по зонам.
        var cellPayload = new List<object>();
        var zoneAgg = new Dictionary<Guid, decimal[]>(); // [occ, cap, thr, cells, occupied]
        decimal totalOcc = 0m, totalCap = 0m; int occupied = 0;

        foreach (var c in cells)
        {
            if (store != null && ZoneStore(c.StoreZone, zones) != store) continue;
            var occ = Get(occByCell, c.MetaId);
            var cap = c.MaxQty;
            var thr = Get(thrByCell, c.MetaId);
            if (occ > 0m) occupied++;
            totalOcc += occ; totalCap += cap;
            cellPayload.Add(new
            {
                id = c.MetaId, name = c.Name,
                line = c.LineNumber, rack = c.RackNumber, shelf = c.ShelfNumber, cell = c.CellNumber,
                zone = zones.TryGetValue(c.StoreZone, out var zz) ? zz.Name : "",
                occ, cap,
                util = cap > 0m ? (decimal?)Math.Round(occ / cap * 100m, 0) : null,
                thr
            });
            var a = zoneAgg.TryGetValue(c.StoreZone, out var av) ? av : new decimal[5];
            a[0] += occ; a[1] += cap; a[2] += thr; a[3] += 1m; if (occ > 0m) a[4] += 1m;
            zoneAgg[c.StoreZone] = a;
        }

        var zonePayload = zoneAgg.Select(kv => new
        {
            id = kv.Key,
            name = zones.TryGetValue(kv.Key, out var z) ? z.Name : "",
            occ = kv.Value[0], cap = kv.Value[1],
            util = kv.Value[1] > 0m ? (decimal?)Math.Round(kv.Value[0] / kv.Value[1] * 100m, 0) : null,
            throughput = kv.Value[2],
            cells = (int)kv.Value[3], occupiedCells = (int)kv.Value[4]
        }).OrderByDescending(z => z.throughput).ToList();

        // Бэклог = незакрытые задачи (свежесозданные имеют Subtype=null, т.е. ещё не проведены).
        int putaway = await _data.CountAsync("PutAwayTask", "[Subtype] IS NULL OR [Subtype] = 'Draft'");
        int pick = await _data.CountAsync("PickTask", "[Subtype] IS NULL OR [Subtype] = 'Draft'");

        return new
        {
            generatedUtc = DateTime.UtcNow,
            kpis = new
            {
                inQty, outQty,
                openTasks = putaway + pick, putawayBacklog = putaway, pickBacklog = pick,
                cellsOccupied = occupied, cellsTotal = cellPayload.Count,
                avgUtil = totalCap > 0m ? (decimal?)Math.Round(totalOcc / totalCap * 100m, 0) : null
            },
            zones = zonePayload,
            cells = cellPayload,
            recent
        };
    }

    private static decimal Get(Dictionary<Guid, decimal> d, Guid k) => d.TryGetValue(k, out var v) ? v : 0m;
    private static Guid ZoneStore(Guid zoneId, Dictionary<Guid, StoreZone> zones) => zones.TryGetValue(zoneId, out var z) ? z.Store : Guid.Empty;
    private static string CellName(Guid id, List<StoreCell> cells) => cells.FirstOrDefault(c => c.MetaId == id)?.Name ?? "?";

    private static Guid AsGuid(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v is null) return Guid.Empty;
        return v is Guid g ? g : Guid.TryParse(v.ToString(), out var p) ? p : Guid.Empty;
    }
    private static decimal AsDec(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var v) && v is not null ? Convert.ToDecimal(v) : 0m;
    private static DateTime AsDate(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var v) && v is not null ? Convert.ToDateTime(v) : DateTime.MinValue;
}
