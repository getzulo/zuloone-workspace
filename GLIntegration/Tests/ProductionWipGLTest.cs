using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// КОМПОНЕНТЫ УХОДЯТ СО СКЛАДА — КНИГА ОБЯЗАНА ЗНАТЬ, ПОКА ОНИ В WIP.
//
// Приход дебетует запасы. Пока заказ Released, qty живёт в WorkInProgress, а
// счёт запасов в книге оставался как после покупки. Finish возвращает стоимость
// в тот же счёт запасов (RM и FG не разведены). Прыжок Draft→Finished
// value-neutral на одном счёте — отдельной проводки нет.
public class ProductionWipGLTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    private sealed class Setup
    {
        public Guid Cell;
        public Guid Component;
        public Guid Product;
        public Guid Supplier;
        public Guid InventoryAccount;
        public Guid WipAccount;
    }

    private string Uniq() => $"{Db.NewId():N}"[..8];

    [IntegrationTest("Запуск в работу дебетует незавершёнку и кредитует запасы")]
    public async Task ReleasePostsWip()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, 10m, 7m);

        var order = await OrderAsync(s, qty: 2m, need: 4m);
        order.Subtype = ProductionOrder.Subtypes.Released;
        await DocumentManager.SaveDocumentAsync(order);

        var wip = await AccountAsync(order.MetaId, s.WipAccount);
        Assert.IsTrue(wip.Debit == 28m, "WIP 4 × 7 = 28, факт {0}", wip.Debit);
        var inv = await AccountAsync(order.MetaId, s.InventoryAccount);
        Assert.IsTrue(inv.Credit == 28m, "запасы кредит 28, факт {0}", inv.Credit);
    }

    [IntegrationTest("Выпуск после запуска возвращает стоимость в запасы и снимает WIP")]
    public async Task FinishReversesWip()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, 10m, 7m);

        var order = await OrderAsync(s, qty: 2m, need: 4m);
        order.Subtype = ProductionOrder.Subtypes.Released;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = ProductionOrder.Subtypes.Finished;
        await DocumentManager.SaveDocumentAsync(order);

        var wip = await AccountAsync(order.MetaId, s.WipAccount);
        Assert.IsTrue(wip.Debit == 28m && wip.Credit == 28m,
            "WIP нетто ноль: Dr 28 / Cr 28, факт {0}/{1}", wip.Debit, wip.Credit);
        var inv = await AccountAsync(order.MetaId, s.InventoryAccount);
        Assert.IsTrue(inv.Debit == 28m && inv.Credit == 28m,
            "запасы нетто ноль: Dr 28 / Cr 28, факт {0}/{1}", inv.Debit, inv.Credit);
    }

    [IntegrationTest("Прыжок Draft→Finished книгу не трогает")]
    public async Task JumpToFinishedSkipsLedger()
    {
        var s = await SetupAsync();
        await ReceiveAsync(s, 10m, 7m);

        var order = await OrderAsync(s, qty: 2m, need: 4m);
        order.Subtype = ProductionOrder.Subtypes.Finished;
        await DocumentManager.SaveDocumentAsync(order);

        var stored = await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId);
        Assert.IsTrue(stored?.Subtype == ProductionOrder.Subtypes.Finished,
            "заказ Finished, факт {0}", stored?.Subtype);
        var wip = await AccountAsync(order.MetaId, s.WipAccount);
        Assert.IsTrue(wip.Debit == 0m && wip.Credit == 0m,
            "прыжок без WIP-журнала, факт {0}/{1}", wip.Debit, wip.Credit);
        var inv = await AccountAsync(order.MetaId, s.InventoryAccount);
        Assert.IsTrue(inv.Debit == 0m && inv.Credit == 0m,
            "запасы прыжка пусты, факт {0}/{1}", inv.Debit, inv.Credit);
    }

    [IntegrationTest("Без счёта WIP заказ проводится, книги нет")]
    public async Task UnconfiguredWipDoesNotBreakPosting()
    {
        var s = await SetupAsync(configureWip: false);
        await ReceiveAsync(s, 10m, 7m);

        var order = await OrderAsync(s, qty: 2m, need: 4m);
        order.Subtype = ProductionOrder.Subtypes.Released;
        await DocumentManager.SaveDocumentAsync(order);

        var stored = await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId);
        Assert.IsTrue(stored?.Subtype == ProductionOrder.Subtypes.Released,
            "Released без профиля, факт {0}", stored?.Subtype);
        Assert.IsTrue((await AccountAsync(order.MetaId, s.InventoryAccount)).Credit == 0m,
            "без WIP-счёта проводки нет");
    }

    private async Task<Setup> SetupAsync(bool configureWip = true)
    {
        await GetService<ITradeProfileService>().SetAsync("Full");
        var today = DateTime.UtcNow.Date;
        var tag = Uniq();

        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = "EUR";
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = "DE";
        country.CodeISO3 = "DEU";
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "ACME GmbH";
        legalEntity.RegistrationNumber = $"REG-WIP-{tag}";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"WH-{tag}";
        divisionType.Name = "Warehouse";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Main";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Central";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "Зона";
        zone.Store = store.MetaId;
        zone.IsBarcodeTracking = false;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"WIP-{tag}";
        cellType.Name = "Storage";
        cellType = await DictionaryManager.SaveRecordAsync(cellType);

        var cell = DictionaryManager.NewRecord<StoreCell>();
        cell.Name = "A-01";
        cell.Type = cellType.MetaId;
        cell.StoreZone = zone.MetaId;
        cell.RackNumber = 1;
        cell.ShelfNumber = 1;
        cell.LineNumber = 1;
        cell.CellNumber = 1;
        cell = await DictionaryManager.SaveRecordAsync(cell);

        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = $"PCS-{tag}";
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"WIP-{tag}";
        group.Name = "Merchandise";
        group = await DictionaryManager.SaveRecordAsync(group);

        var component = DictionaryManager.NewRecord<Item>();
        component.Name = "Widget";
        component.ItemGroup = group.MetaId;
        component.UnitOfMeasure = uom.MetaId;
        component = await DictionaryManager.SaveRecordAsync(component);

        var product = DictionaryManager.NewRecord<Item>();
        product.Name = "Assembly";
        product.ItemGroup = group.MetaId;
        product.UnitOfMeasure = uom.MetaId;
        product = await DictionaryManager.SaveRecordAsync(product);

        var supplier = DictionaryManager.NewRecord<Supplier>();
        supplier.Name = "Supply Co";
        supplier = await DictionaryManager.SaveRecordAsync(supplier);

        var invCode = $"14{tag}"[..8];
        var wipCode = $"15{tag}"[..8];
        var payCode = $"20{tag}"[..8];
        var inventoryAccount = await NewAccountAsync(invCode, "Inventory", AccountType.Asset, currency.MetaId);
        var wipAccount = await NewAccountAsync(wipCode, "WIP", AccountType.Asset, currency.MetaId);
        await NewAccountAsync(payCode, "AP", AccountType.Liability, currency.MetaId);

        var accRows = await DictionaryManager.GetRecordsAsync<AccountingSettings>(null, 1);
        var settings = accRows.Count > 0 ? accRows[0] : DictionaryManager.NewRecord<AccountingSettings>();
        settings.InventoryAccountCode = invCode;
        settings.PayableAccountCode = payCode;
        settings.WipAccountCode = configureWip ? wipCode : string.Empty;
        settings.WipAccount = configureWip ? wipAccount : Guid.Empty;
        await DictionaryManager.SaveRecordAsync(settings);

        var fiscalYear = DictionaryManager.NewRecord<FiscalYear>();
        fiscalYear.Code = $"FY-{tag}";
        fiscalYear.StartDate = today.AddMonths(-6);
        fiscalYear.EndDate = today.AddMonths(6);
        fiscalYear.IsClosed = false;
        fiscalYear = await DictionaryManager.SaveRecordAsync(fiscalYear);

        var fiscalPeriod = DictionaryManager.NewRecord<FiscalPeriod>();
        fiscalPeriod.Code = $"P-{tag}";
        fiscalPeriod.FiscalYear = fiscalYear.MetaId;
        fiscalPeriod.FromDate = today.AddDays(-15);
        fiscalPeriod.ToDate = today.AddDays(15);
        fiscalPeriod.Status = "Open";
        await DictionaryManager.SaveRecordAsync(fiscalPeriod);

        return new Setup
        {
            Cell = cell.MetaId,
            Component = component.MetaId,
            Product = product.MetaId,
            Supplier = supplier.MetaId,
            InventoryAccount = inventoryAccount,
            WipAccount = wipAccount,
        };
    }

    private static async Task<Guid> NewAccountAsync(string code, string name, AccountType type, Guid currency)
    {
        var account = DictionaryManager.NewRecord<ChartOfAccounts>();
        account.Code = code;
        account.Name = name;
        account.AccountType = type;
        account.IsPostable = true;
        account.Currency = currency;
        return (await DictionaryManager.SaveRecordAsync(account)).MetaId;
    }

    private static async Task ReceiveAsync(Setup s, decimal qty, decimal price)
    {
        var order = await DocumentManager.NewDocumentAsync<PurchaseOrder>();
        order.Supplier = s.Supplier;
        order.Location = s.Cell;
        order.Lines.Add(new PurchaseOrderLinesTablePartRow { Item = s.Component, Quantity = qty, UnitPrice = price });
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Ordered;
        await DocumentManager.SaveDocumentAsync(order);
        order.Subtype = PurchaseOrder.Subtypes.Received;
        await DocumentManager.SaveDocumentAsync(order);
    }

    private static async Task<ProductionOrder> OrderAsync(Setup s, decimal qty, decimal need)
    {
        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = s.Product;
        order.Quantity = qty;
        order.Location = s.Cell;
        order.Components.Add(new ProductionOrderComponentsTablePartRow { Component = s.Component, QtyRequired = need });
        await DocumentManager.SaveDocumentAsync(order);
        return order;
    }

    private static async Task<(decimal Debit, decimal Credit)> AccountAsync(Guid document, Guid account)
    {
        decimal debit = 0m, credit = 0m;
        var family = await DocumentManager.GetDocumentFamilyAsync(document);
        var children = family.Edges.Where(e => e.ParentDocId == document).Select(e => e.ChildDocId).Distinct();
        foreach (var childId in children)
        {
            var entry = await DocumentManager.GetDocumentAsync<JournalEntry>(childId);
            if (entry == null) continue;
            foreach (var line in entry.Lines.Where(l => l.Account == account))
            {
                debit += line.Debit;
                credit += line.Credit;
            }
        }
        return (debit, credit);
    }
}
