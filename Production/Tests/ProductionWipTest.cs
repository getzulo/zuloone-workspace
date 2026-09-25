using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

public class ProductionWipTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static IBomService Bom => GetService<IBomService>();
    private static ITotalsManager Totals => GetService<ITotalsManager>();

    private Guid _cell;
    private Guid _group;
    private Guid _uom;

    private async Task PartyAsync()
    {
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

        var le = DictionaryManager.NewRecord<LegalEntity>();
        le.Name = "ACME GmbH";
        le.RegistrationNumber = $"REG-WIP-{Db.NewId():N}"[..16];
        le.Country = country.MetaId;
        le.Currency = currency.MetaId;
        le = await DictionaryManager.SaveRecordAsync(le);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = "WH";
        divisionType.Name = "Warehouse";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Main";
        division.LegalEntity = le.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Central";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);

        var zone = DictionaryManager.NewRecord<StoreZone>();
        zone.Name = "Zone";
        zone.Store = store.MetaId;
        zone = await DictionaryManager.SaveRecordAsync(zone);

        var cellType = DictionaryManager.NewRecord<StoreCellType>();
        cellType.Code = $"STG-{Db.NewId():N}"[..12];
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
        _cell = (await DictionaryManager.SaveRecordAsync(cell)).MetaId;

        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = "PCS";
        _uom = (await DictionaryManager.SaveRecordAsync(uom)).MetaId;

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"G-{Db.NewId():N}"[..12];
        group.Name = "Goods";
        _group = (await DictionaryManager.SaveRecordAsync(group)).MetaId;
    }

    private async Task<Guid> ItemAsync(string name)
    {
        var item = DictionaryManager.NewRecord<Item>();
        item.Name = name;
        item.ItemGroup = _group;
        item.UnitOfMeasure = _uom;
        return (await DictionaryManager.SaveRecordAsync(item)).MetaId;
    }

    private async Task SeedBomAndOrderAsync(Guid product, Guid component, decimal qty)
    {
        var bom = DictionaryManager.NewRecord<BillOfMaterials>();
        bom.Name = $"BOM-{product:N}"[..16];
        bom.Product = product;
        bom.OutputQty = qty;
        bom = await DictionaryManager.SaveRecordAsync(bom);
        var c = DictionaryManager.NewRecord<BomComponent>();
        c.Bom = bom.MetaId;
        c.Component = component;
        c.QtyPer = 1m;
        c.Explode = false;
        await DictionaryManager.SaveRecordAsync(c);

        await Db.PostMovementAsync("Stock", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Item"] = component, ["Cell"] = _cell },
            new Dictionary<string, decimal> { ["Qty"] = 1m });
    }

    private async Task<ProductionOrder> OrderAsync(Guid product, decimal qty)
    {
        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = product;
        order.Quantity = qty;
        order.Location = _cell;
        order.OutputLocation = _cell;
        await DocumentManager.SaveDocumentAsync(order);
        var full = (await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId))!;
        var need = await Bom.ExpandByProductAsync(product, qty);
        full.Components.Clear();
        foreach (var kv in need)
            full.Components.Add(new ProductionOrderComponentsTablePartRow { Component = kv.Key, QtyRequired = kv.Value });
        await DocumentManager.SaveDocumentAsync(full);
        return (await DocumentManager.GetDocumentAsync<ProductionOrder>(full.MetaId))!;
    }

    private Task<decimal> WipAsync(Guid item)
        => Totals.GetBalanceAsync("WorkInProgress", "Qty",
            new Dictionary<string, object?> { ["Item"] = item, ["Cell"] = _cell });

    private async Task<decimal> OnHandAsync(Guid item)
    {
        var bal = await Totals.GetBalanceAsync("Stock",
            new Dictionary<string, object?> { ["Item"] = item, ["Cell"] = _cell });
        return bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
    }

    [IntegrationTest("Released пишет WIP, Finished обнуляет")]
    public async Task ReleasedWritesWipFinishClears()
    {
        await PartyAsync();
        var flour = await ItemAsync("Flour");
        var bread = await ItemAsync("Bread");
        await SeedBomAndOrderAsync(bread, flour, 1m);
        var order = await OrderAsync(bread, 1m);

        var releaseId = await Db.FindCommandIdAsync("document", "ReleaseProduction");
        var released = await Db.ExecuteDocumentCommandAsync(releaseId, order.MetaId);
        Assert.IsTrue(released.Success, "запуск: {0}", string.Join("; ", released.ClientMessages));
        Assert.IsTrue(await WipAsync(bread) == 1m, "WIP 1 после Released, факт {0}", await WipAsync(bread));
        Assert.IsTrue(await OnHandAsync(bread) == 0m, "изделия ещё нет");

        var finishId = await Db.FindCommandIdAsync("document", "FinishProduction");
        var finished = await Db.ExecuteDocumentCommandAsync(finishId, order.MetaId);
        Assert.IsTrue(finished.Success, "выпуск: {0}", string.Join("; ", finished.ClientMessages));
        Assert.IsTrue(await WipAsync(bread) == 0m, "WIP снят Mix, факт {0}", await WipAsync(bread));
        Assert.IsTrue(await OnHandAsync(bread) == 1m, "изделие на ячейке, факт {0}", await OnHandAsync(bread));
    }

    [IntegrationTest("Прыжок Draft→Finished не создаёт WIP")]
    public async Task DirectFinishLeavesWipEmpty()
    {
        await PartyAsync();
        var flour = await ItemAsync("Flour");
        var bread = await ItemAsync("Bread");
        await SeedBomAndOrderAsync(bread, flour, 1m);
        var order = await OrderAsync(bread, 1m);

        order.Subtype = ProductionOrder.Subtypes.Finished;
        await DocumentManager.SaveDocumentAsync(order);

        Assert.IsTrue(await WipAsync(bread) == 0m, "без Released WIP нет, факт {0}", await WipAsync(bread));
        Assert.IsTrue(await OnHandAsync(bread) == 1m, "изделие всё равно выпущено, факт {0}", await OnHandAsync(bread));
    }
}
