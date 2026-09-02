#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// ═══ ВЫБЫТИЕ ЗАПАСОВ МИМО ПРОДАЖИ — В ГЛАВНУЮ КНИГУ ══════════════════════════
//
// Приход дебетует счёт запасов, продажа кредитует его через себестоимость. Бой,
// недостача и отпуск со склада не попадали в книгу ВООБЩЕ: стоимость уходила из
// регистра ItemCostFifo и оставалась на счёте запасов навсегда, так что книга
// завышала запас на всё списанное за историю.
//
// Логика одна на два документа (корректировка остатков и отпуск), поэтому живёт
// в сервисе, а обработчики документов только решают КОГДА её звать.
//
// Сумма — ФАКТ списания из движений ItemCostFifo этого документа, а не пересчёт
// по строкам: метод оценки (FIFO/AVG) живёт в настройках Costing, и повторять
// его здесь значит разъехаться с учётом запаса в день смены настройки. Выбытие
// приходит отрицательной суммой (движок подставляет туда себестоимость слоёв) —
// в проводку идёт модуль. Положительные движения (излишек) дают ноль и проводки
// не порождают.
//
// Счёт списания СВОЙ, не COGS: себестоимость продаж — это стоимость проданного,
// и валовая маржа считается по ней. Свалив туда потери, мы исказили бы маржу.
public partial class InventoryWriteOffGLService
{
    private readonly ITotalsManager _totals;
    private readonly IDictionaryManager<StoreCell> _cells;
    private readonly IDictionaryManager<StoreZone> _zones;
    private readonly IDictionaryManager<Store> _stores;
    private readonly IDictionaryManager<Division> _divisions;
    private readonly IDictionaryManager<LegalEntity> _entities;

    public InventoryWriteOffGLService(
        ITotalsManager totals,
        IDictionaryManager<StoreCell> cells,
        IDictionaryManager<StoreZone> zones,
        IDictionaryManager<Store> stores,
        IDictionaryManager<Division> divisions,
        IDictionaryManager<LegalEntity> entities)
    {
        _totals = totals;
        _cells = cells;
        _zones = zones;
        _stores = stores;
        _divisions = divisions;
        _entities = entities;
    }

    /// <summary>
    /// Разнести списанную документом себестоимость. Возвращает id проводки или
    /// null, если списывать нечего либо счета не настроены.
    /// </summary>
    public async Task<Guid?> PostAsync(Guid documentMetaId, Guid cell, string description)
    {
        var gl = ScriptServices.Get<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;
        if (string.IsNullOrWhiteSpace(settings.InventoryWriteOffAccountCode)) return null;

        var cost = 0m;
        foreach (var row in await _totals.QueryMovementsAsync(
            "ItemCostFifo", $"[DocumentMetaId] = '{documentMetaId}'"))
        {
            if (row.TryGetValue("Amount", out var amount) && amount != null)
                cost -= Convert.ToDecimal(amount);
        }
        if (cost <= 0m) return null;

        var le = await ResolveLegalEntityAsync(cell);
        if (le == null) return null;

        return await gl.PostAsync(
            DateTime.UtcNow.Date, le.MetaId, le.Currency, cost,
            settings.InventoryWriteOffAccountCode, settings.InventoryAccountCode,
            description,
            "Списание запасов", "Выбытие запасов");
    }

    /// <summary>Юрлицо — по цепочке Ячейка → Зона → Склад → Подразделение → Юрлицо.</summary>
    private async Task<LegalEntity?> ResolveLegalEntityAsync(Guid cell)
    {
        var loc = await _cells.GetRecordAsync(cell);
        if (loc == null) return null;
        var zone = await _zones.GetRecordAsync(loc.StoreZone);
        if (zone == null) return null;
        var store = await _stores.GetRecordAsync(zone.Store);
        if (store == null) return null;
        var div = await _divisions.GetRecordAsync(store.Division);
        if (div == null) return null;
        return await _entities.GetRecordAsync(div.LegalEntity);
    }
}
