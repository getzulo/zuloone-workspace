#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// ═══ NON-SALE INVENTORY DISPOSAL — INTO THE GENERAL LEDGER ══════════════════
//
// Receipt debits the inventory account, a sale credits it through COGS.
// Breakage, shortage and a warehouse issue never hit the books AT ALL: cost
// left the ItemCostFifo register and stayed on the inventory account forever,
// so the books overstated inventory by everything written off over history.
//
// The logic is shared by two documents (stock adjustment and goods issue),
// so it lives in a service, and document handlers only decide WHEN to call it.
//
// The amount is the FACT from this document's ItemCostFifo movements, not a
// recalculation from lines: the valuation method (FIFO/AVG) lives in Costing
// settings, and repeating it here means drift from inventory accounting on
// the day the setting changes. Disposal arrives as a negative amount (the
// engine puts layer cost there) — the write-off posting takes the absolute
// value. Positive movements are surplus: Costing opens a lot, and the same
// amount goes into the second leg (PostSurplusAsync).
//
// The write-off account is ITS OWN, not COGS: cost of goods sold is the cost
// of what was sold, and gross margin is computed from it. Dumping losses
// there would distort the margin.
//
// Surplus is INCOME, not a write-off reversal. The loss account and COGS stay
// clean: only a real inventory exit lands there. Dumping a find onto the same
// account would wipe breakage with receipts and margin/losses would stop
// being readable.
// So its own leg: Dr inventory / Cr surplus income. A zero lot (the item was
// never purchased) — no amount, no posting: there is nothing to invent income
// from.
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
    /// Post the cost this document wrote off. Returns the journal-entry id or
    /// null if there is nothing to write off or the accounts are not configured.
    /// </summary>
    /// <param name="date">DOCUMENT date, not the posting-day date. From it
    /// <see cref="IGeneralLedgerService.PostAsync"/> picks the fiscal period,
    /// so a back-dated document must take its own date into the books —
    /// otherwise register movements land in one period and the journal entry
    /// in another, and period reconciliation drifts. The parameter is
    /// required on purpose: defaulting to today is exactly the bug it is
    /// meant to prevent.</param>
    public async Task<Guid?> PostAsync(Guid documentMetaId, Guid cell, DateTime date, string description)
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
            date, le.MetaId, le.Currency, cost,
            settings.InventoryWriteOffAccountCode, settings.InventoryAccountCode,
            description,
            "Списание запасов", "Выбытие запасов");
    }

    /// <summary>
    /// Post the surplus cost that Costing opened as a lot. Returns the
    /// journal-entry id or null if there is nothing to receive or the accounts
    /// are not configured.
    /// </summary>
    /// <remarks>
    /// Amount — positive Amount values on this document's ItemCostFifo
    /// (equivalent: if cost = −Σ Amount and cost &lt; 0, then surplus = −cost).
    /// Zero surplus lots (no purchase history) yield 0 and do not get here.
    /// </remarks>
    public async Task<Guid?> PostSurplusAsync(Guid documentMetaId, Guid cell, DateTime date, string description)
    {
        var gl = ScriptServices.Get<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;
        if (string.IsNullOrWhiteSpace(settings.InventorySurplusAccountCode)) return null;
        if (string.IsNullOrWhiteSpace(settings.InventoryAccountCode)) return null;

        var surplus = 0m;
        foreach (var row in await _totals.QueryMovementsAsync(
            "ItemCostFifo", $"[DocumentMetaId] = '{documentMetaId}'"))
        {
            if (row.TryGetValue("Amount", out var amount) && amount != null)
            {
                var a = Convert.ToDecimal(amount);
                if (a > 0m) surplus += a;
            }
        }
        if (surplus <= 0m) return null;

        var le = await ResolveLegalEntityAsync(cell);
        if (le == null) return null;

        return await gl.PostAsync(
            date, le.MetaId, le.Currency, surplus,
            settings.InventoryAccountCode, settings.InventorySurplusAccountCode,
            description,
            "Приход запасов (излишек)", "Доход от излишка");
    }

    /// <summary>Legal entity — along Cell → Zone → Store → Division → LegalEntity.</summary>
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
