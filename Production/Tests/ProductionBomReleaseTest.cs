using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

public class ProductionBomReleaseTest : IntegrationTestScriptBase
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
        le.RegistrationNumber = $"REG-PR-{Db.NewId():N}"[..16];
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

    private async Task<Guid> BomAsync(Guid product, decimal outputQty, params (Guid Component, decimal Qty, bool Explode)[] lines)
    {
        var bom = DictionaryManager.NewRecord<BillOfMaterials>();
        bom.Name = $"BOM-{product:N}"[..16];
        bom.Product = product;
        bom.OutputQty = outputQty;
        bom = await DictionaryManager.SaveRecordAsync(bom);
        foreach (var line in lines)
        {
            var c = DictionaryManager.NewRecord<BomComponent>();
            c.Bom = bom.MetaId;
            c.Component = line.Component;
            c.QtyPer = line.Qty;
            c.Explode = line.Explode;
            await DictionaryManager.SaveRecordAsync(c);
        }
        return bom.MetaId;
    }

    private Task SeedStockAsync(Guid item, decimal qty)
        => Db.PostMovementAsync("Stock", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Item"] = item, ["Cell"] = _cell },
            new Dictionary<string, decimal> { ["Qty"] = qty });

    private async Task<decimal> OnHandAsync(Guid item)
    {
        var bal = await Totals.GetBalanceAsync("Stock",
            new Dictionary<string, object?> { ["Item"] = item, ["Cell"] = _cell });
        return bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
    }

    private async Task<ProductionOrder> OrderAsync(Guid product, decimal qty)
    {
        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = product;
        order.Quantity = qty;
        order.Location = _cell;
        order.OutputLocation = _cell;
        await DocumentManager.SaveDocumentAsync(order);
        return (await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId))!;
    }

    [IntegrationTest("Без Explode полуфабрикат остаётся строкой, а не разбирается")]
    public async Task NoExplodeKeepsTheSemiFinished()
    {
        await PartyAsync();
        var flour = await ItemAsync("Flour");
        var dough = await ItemAsync("Dough");
        var bread = await ItemAsync("Bread");
        await BomAsync(dough, 1m, (flour, 10m, false));
        await BomAsync(bread, 1m, (dough, 1m, false));

        var need = await Bom.ExpandByProductAsync(bread, 2m);
        Assert.IsTrue(need.Count == 1 && need.ContainsKey(dough) && need[dough] == 2m,
            "без Explode в потребности только тесто 2, факт: {0}",
            string.Join(",", need.Select(kv => $"{kv.Key:N}={kv.Value}")));
    }

    [IntegrationTest("Explode раскрывает полуфабрикат до сырья")]
    public async Task ExplodeWalksTheChildBom()
    {
        await PartyAsync();
        var flour = await ItemAsync("Flour");
        var dough = await ItemAsync("Dough");
        var bread = await ItemAsync("Bread");
        await BomAsync(dough, 1m, (flour, 10m, false));
        await BomAsync(bread, 1m, (dough, 1m, true));

        var need = await Bom.ExpandByProductAsync(bread, 2m);
        Assert.IsTrue(need.Count == 1 && need.ContainsKey(flour) && need[flour] == 20m,
            "два хлеба → 20 муки, не тесто; факт: {0}",
            string.Join(",", need.Select(kv => $"{kv.Value}")));
        Assert.IsTrue(!need.ContainsKey(dough), "тесто с Explode в потребность не входит");
    }

    [IntegrationTest("Цикл разворачиваемых спецификаций отклоняется")]
    public async Task ExplodeCycleIsRejected()
    {
        await PartyAsync();
        var a = await ItemAsync("A");
        var b = await ItemAsync("B");
        await BomAsync(a, 1m, (b, 1m, true));
        await BomAsync(b, 1m, (a, 1m, true));

        var reason = string.Empty;
        try { await Bom.ExpandByProductAsync(a, 1m); }
        catch (Exception ex) { reason = ex.Message; }
        Assert.IsTrue(reason.Contains("Цикл"),
            "взаимный Explode обязан отказать, факт: {0}", reason);
    }

    [IntegrationTest("Released списывает комплектующие, Finished приходует изделие")]
    public async Task ReleasedConsumesFinishedOutputs()
    {
        await PartyAsync();
        var flour = await ItemAsync("Flour");
        var bread = await ItemAsync("Bread");
        await BomAsync(bread, 1m, (flour, 10m, false));
        await SeedStockAsync(flour, 10m);

        var order = await OrderAsync(bread, 1m);
        var need = await Bom.ExpandByProductAsync(bread, 1m);
        var full = await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId);
        full!.Components.Clear();
        foreach (var kv in need)
            full.Components.Add(new ProductionOrderComponentsTablePartRow { Component = kv.Key, QtyRequired = kv.Value });
        await DocumentManager.SaveDocumentAsync(full);

        var releaseId = await Db.FindCommandIdAsync("document", "ReleaseProduction");
        var released = await Db.ExecuteDocumentCommandAsync(releaseId, order.MetaId);
        Assert.IsTrue(released.Success, "запуск в работу: {0}", string.Join("; ", released.ClientMessages));

        var afterRelease = await DocumentManager.GetDocumentAsync<ProductionOrder>(order.MetaId);
        Assert.IsTrue(afterRelease!.Subtype == ProductionOrder.Subtypes.Released,
            "после запуска подтип Released, факт {0}", afterRelease.Subtype);
        Assert.IsTrue(await OnHandAsync(flour) == 0m, "мука списана на Released, факт {0}", await OnHandAsync(flour));
        Assert.IsTrue(await OnHandAsync(bread) == 0m, "изделия на Released ещё нет, факт {0}", await OnHandAsync(bread));

        var finishId = await Db.FindCommandIdAsync("document", "FinishProduction");
        var finished = await Db.ExecuteDocumentCommandAsync(finishId, order.MetaId);
        var finishText = finished.ClientMessages == null ? "null-msgs" : string.Join(" | ", finished.ClientMessages);
        Assert.IsTrue(finished.Success,
            "finish failed success={0} cmd={1} msg={2} err={3} msgs={4}",
            finished.Success, finishId, finished.Message, finished.ErrorDetails, finishText);

        Assert.IsTrue(await OnHandAsync(flour) == 0m, "после выпуска мука не вернулась, факт {0}", await OnHandAsync(flour));
        Assert.IsTrue(await OnHandAsync(bread) == 1m, "изделие на ячейке, факт {0}", await OnHandAsync(bread));
    }
}
