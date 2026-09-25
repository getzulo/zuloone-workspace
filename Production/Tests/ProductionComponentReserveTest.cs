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
