#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

// ═══ СЕБЕСТОИМОСТЬ БЕЗВОЗМЕЗДНОГО ПРИХОДА ════════════════════════════════════
//
// Драйвер CostingIssue закрывает только РАСХОДНУЮ сторону: уменьшился складской
// остаток — списалась себестоимость. Приходную он намеренно не трогает, потому
// что положительное нетто бывает разной природы: заказ поставщику заводит партию
// сам (ReceiptFifoTx), выпуск производства — сам (ProductionOrderEventHandler),
// а излишек и пересчёт вверх не заводили её НИКТО. Товар появлялся на складе без
// партии и потом молча списывался по нулю: Math.Min(-net, onHand) в драйвере
// брал ноль наличного в партиях, и стоимость запаса не уменьшалась вовсе.
//
// ЧЕМ оценивать излишек. Покупной цены у него нет по определению — товар не
// покупали, а нашли. Берётся ТЕКУЩАЯ СРЕДНЯЯ товара (Amount/Quantity по открытым
// партиям): найденные единицы того же товара стоят столько же, сколько уже
// лежащие. Средняя от этого не меняется — приход по средней её сохраняет, и
// оценка запаса растёт ровно на стоимость найденного.
//
// Партий нет вовсе (товар никогда не покупали) — цены нет и взяться ей неоткуда,
// партия заводится нулевой. Это честный ноль, а не потерянная стоимость: в
// системе нет ни одного факта о том, сколько этот товар стоит.
//
// ПОЧЕМУ ПО ДВИЖЕНИЯМ, А НЕ ПО СТРОКАМ ДОКУМЕНТА. Строки у корректировки и у
// инвентаризации разные (Quantity против CountedQty, у второй ещё и дельта к
// остатку), а движения Stock — одни и те же и уже нормализованы в базовую
// единицу. Считая нетто по движениям документа, сервис работает одинаково для
// обоих и переживёт любой третий документ такого рода.
public partial class SurplusCostingService
{
    private readonly ITotalsManager _totals;
    private readonly IRegisterMovementService _movements;
    private readonly IMetadataService _metadata;

    public SurplusCostingService(
        ITotalsManager totals,
        IRegisterMovementService movements,
        IMetadataService metadata)
    {
        _totals = totals;
        _movements = movements;
        _metadata = metadata;
    }

    /// <summary>
    /// Завести партии себестоимости на всё, что документ добавил на склад сверх
    /// того, что списал. Возвращает заведённую стоимость (0 — приходовать нечего).
    /// </summary>
    public async Task<decimal> CaptureSurplusAsync(Guid documentMetaId, DateTime movementDate)
    {
        // Нетто по товару в пределах документа: перемещение (−из ячейки, +в
        // ячейку) даёт ноль и партий не заводит, ровно как не списывает их драйвер.
        var net = new Dictionary<Guid, decimal>();
        foreach (var row in await _totals.QueryMovementsAsync("Stock", $"[DocumentMetaId] = '{documentMetaId}'"))
        {
            if (row["Item"] is null) continue;
            var item = (Guid)row["Item"]!;
            net[item] = (net.TryGetValue(item, out var acc) ? acc : 0m) + Convert.ToDecimal(row["Qty"]);
        }

        var surplus = net.Where(kv => kv.Value > 0m).ToList();
        if (surplus.Count == 0) return 0m;

        var inventoryValueId = (await _metadata.GetAllRegistersAsync())
            .First(r => string.Equals(r.Name, "InventoryValue", StringComparison.OrdinalIgnoreCase)).MetaId;

        var captured = 0m;
        foreach (var (item, qty) in surplus)
        {
            var key = new Dictionary<string, object?> { ["Item"] = item };

            var onHand = await _totals.GetBalanceAsync("ItemCostFifo", "Quantity", key);
            var value = await _totals.GetBalanceAsync("ItemCostFifo", "Amount", key);
            var unitCost = onHand > 0m ? value / onHand : 0m;
            var amount = Math.Round(unitCost * qty, 2, MidpointRounding.AwayFromZero);

            await _totals.PostMovementAsync("ItemCostFifo", documentMetaId, movementDate, key,
                new Dictionary<string, decimal> { ["Quantity"] = qty, ["Amount"] = amount });

            // InventoryValue разрезан ДИНАМИЧЕСКОЙ аналитикой Item, а
            // ITotalsManager.PostMovementAsync аналитики не принимает — эта
            // проводка идёт через движок регистров, как и у драйвера списания.
            await _movements.PostMovementAsync(
                inventoryValueId, documentMetaId, movementDate,
                new Dictionary<string, object?>(),
                new Dictionary<string, decimal> { ["Qty"] = qty, ["Value"] = amount },
                analytics: new Dictionary<string, object?> { ["Item"] = item });

            captured += amount;
        }

        return captured;
    }
}
