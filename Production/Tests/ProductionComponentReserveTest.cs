using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Черновик не трогает Stock, но компоненты уже обещаны этому заказу.
// Свободный остаток продажи — Stock минус ReservedStock, поэтому тот же
// срез виден ISalesFulfillmentService.AvailableQtyAsync.
public class ProductionComponentReserveTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();
    private static ISalesFulfillmentService Fulfillment => GetService<ISalesFulfillmentService>();

    private static Task<decimal> OnHandAsync(Guid cell, Guid item)
        => TotalsManager.GetBalanceAsync("Stock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item });

    private static Task<decimal> ReservedAsync(Guid cell, Guid item)
        => TotalsManager.GetBalanceAsync("ReservedStock", "Qty",
            new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item });

    [IntegrationTest("Черновик держит компоненты, запуск списывает их и снимает резерв")]
    public async Task DraftHoldsComponentsUntilRelease()
    {
        var loc = Db.NewId();
        var comp = await NewItemAsync("Компонент-резерв");
        var product = await NewItemAsync("Изделие-резерв");
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = loc, ["Item"] = comp.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        var order = await NewOrderAsync(product.MetaId, loc, comp.MetaId, 4m);
        Assert.IsTrue(await OnHandAsync(loc, comp.MetaId) == 10m, "черновик не списывает компонент");
        Assert.IsTrue(await ReservedAsync(loc, comp.MetaId) == 4m, "черновик держит 4, факт {0}", await ReservedAsync(loc, comp.MetaId));
        Assert.IsTrue(await Fulfillment.AvailableQtyAsync(loc, comp.MetaId) == 6m,
            "продаже свободно 6, факт {0}", await Fulfillment.AvailableQtyAsync(loc, comp.MetaId));

        order.Components[0].QtyRequired = 6m;
        await DocumentManager.SaveDocumentAsync(order);
        Assert.IsTrue(await ReservedAsync(loc, comp.MetaId) == 6m,
            "правка строки пересчитывает резерв, факт {0}", await ReservedAsync(loc, comp.MetaId));

        order.Subtype = ProductionOrder.Subtypes.Released;
        await DocumentManager.SaveDocumentAsync(order);
        Assert.IsTrue(await OnHandAsync(loc, comp.MetaId) == 4m, "запуск списывает 6, на ячейке 4");
        Assert.IsTrue(await ReservedAsync(loc, comp.MetaId) == 0m,
            "после списания резерв не висит, факт {0}", await ReservedAsync(loc, comp.MetaId));
        Assert.IsTrue(await Fulfillment.AvailableQtyAsync(loc, comp.MetaId) == 4m,
            "свободно столько, сколько осталось на ячейке, факт {0}", await Fulfillment.AvailableQtyAsync(loc, comp.MetaId));
    }

    [IntegrationTest("Выпуск из черновика снимает резерв вместе со списанием")]
    public async Task FinishFromDraftReleasesTheHold()
    {
        var loc = Db.NewId();
        var comp = await NewItemAsync("Компонент-выпуск");
        var product = await NewItemAsync("Изделие-выпуск");
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = loc, ["Item"] = comp.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        var order = await NewOrderAsync(product.MetaId, loc, comp.MetaId, 4m);
        Assert.IsTrue(await ReservedAsync(loc, comp.MetaId) == 4m, "до выпуска резерв 4");

        order.Subtype = ProductionOrder.Subtypes.Finished;
        await DocumentManager.SaveDocumentAsync(order);
        Assert.IsTrue(await OnHandAsync(loc, comp.MetaId) == 6m, "выпуск списывает 4, на ячейке 6");
        Assert.IsTrue(await ReservedAsync(loc, comp.MetaId) == 0m,
            "выпуск из черновика не оставляет резерв, факт {0}", await ReservedAsync(loc, comp.MetaId));
    }

    [IntegrationTest("Чужой резерв не мешает запуску, пока свободного хватает на свою потребность")]
    public async Task ForeignHoldLeavesReleaseWhenFreeCovers()
    {
        var loc = Db.NewId();
        var comp = await NewItemAsync("Компонент-соседи");
        var product = await NewItemAsync("Изделие-соседи");
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = loc, ["Item"] = comp.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        await NewOrderAsync(product.MetaId, loc, comp.MetaId, 4m);
        var mine = await NewOrderAsync(product.MetaId, loc, comp.MetaId, 4m);
        Assert.IsTrue(await ReservedAsync(loc, comp.MetaId) == 8m, "два черновика держат 8");

        mine.Subtype = ProductionOrder.Subtypes.Released;
        await DocumentManager.SaveDocumentAsync(mine);
        Assert.IsTrue(await OnHandAsync(loc, comp.MetaId) == 6m, "свой запуск списывает 4, на ячейке 6");
        Assert.IsTrue(await ReservedAsync(loc, comp.MetaId) == 4m,
            "чужой черновик остаётся, факт {0}", await ReservedAsync(loc, comp.MetaId));
    }

    [IntegrationTest("Чужой резерв отклоняет запуск, если свободного меньше потребности")]
    public async Task ForeignHoldRejectsRelease()
    {
        var loc = Db.NewId();
        var comp = await NewItemAsync("Компонент-занято");
        var product = await NewItemAsync("Изделие-занято");
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = loc, ["Item"] = comp.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        await NewOrderAsync(product.MetaId, loc, comp.MetaId, 8m);
        var mine = await NewOrderAsync(product.MetaId, loc, comp.MetaId, 4m);
        Assert.IsTrue(await ReservedAsync(loc, comp.MetaId) == 12m, "перед отказом резерв 12");

        var detail = "";
        var rejected = false;
        try
        {
            mine.Subtype = ProductionOrder.Subtypes.Released;
            await DocumentManager.SaveDocumentAsync(mine);
        }
        catch (Exception ex)
        {
            rejected = true;
            detail = ex.ToString();
        }
        Assert.IsTrue(rejected && detail.Contains("требуется 4.0000, в наличии 2.0000"),
            "нужно 4, свободно 2, факт {0}", detail);
    }

    [IntegrationTest("Выпуск после запуска не забирает чужой резерв с оставшегося остатка")]
    public async Task FinishAfterReleaseLeavesForeignHold()
    {
        var loc = Db.NewId();
        var comp = await NewItemAsync("Компонент-хвост");
        var product = await NewItemAsync("Изделие-хвост");
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = loc, ["Item"] = comp.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        var mine = await NewOrderAsync(product.MetaId, loc, comp.MetaId, 6m);
        mine.Subtype = ProductionOrder.Subtypes.Released;
        await DocumentManager.SaveDocumentAsync(mine);
        await NewOrderAsync(product.MetaId, loc, comp.MetaId, 4m);
        Assert.IsTrue(await OnHandAsync(loc, comp.MetaId) == 4m, "после запуска на ячейке 4");
        Assert.IsTrue(await ReservedAsync(loc, comp.MetaId) == 4m, "хвост держит чужой черновик");

        mine.Subtype = ProductionOrder.Subtypes.Finished;
        await DocumentManager.SaveDocumentAsync(mine);
        Assert.IsTrue(await OnHandAsync(loc, comp.MetaId) == 4m, "выпуск не списывает хвост повторно");
        Assert.IsTrue(await ReservedAsync(loc, comp.MetaId) == 4m,
            "чужой резерв на хвосте цел, факт {0}", await ReservedAsync(loc, comp.MetaId));
    }

    private async Task<Item> NewItemAsync(string name)
    {
        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = "PCS";
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = $"PR-{Db.NewId():N}"[..12];
        group.Name = "Производство";
        group = await DictionaryManager.SaveRecordAsync(group);

        var item = DictionaryManager.NewRecord<Item>();
        item.Name = name;
        item.ItemGroup = group.MetaId;
        item.UnitOfMeasure = uom.MetaId;
        return await DictionaryManager.SaveRecordAsync(item);
    }

    private static async Task<ProductionOrder> NewOrderAsync(Guid product, Guid location, Guid component, decimal qtyRequired)
    {
        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = product;
        order.Quantity = 1m;
        order.Location = location;
        order.OutputLocation = location;
        order.Components.Add(new ProductionOrderComponentsTablePartRow { Component = component, QtyRequired = qtyRequired });
        await DocumentManager.SaveDocumentAsync(order);
        return order;
    }
}
