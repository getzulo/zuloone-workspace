using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// ProductionFlowTest уже покрывает Stock (компонент списан, изделие оприходовано).
// Здесь — новый шов: выпуск обязан завести ItemCostFifo-партию самому изделию
// (ProductionOrderEventHandler.OnAfterPostAsync), а не только списать компоненты
// (это уже делает CostingIssueTotalDriver на Stock). InventoryValue должен при
// этом остаться нулевым по сумме — Costing переносит стоимость, а не создаёт её.
public class ProductionOutputCostTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private async Task<Func<string, Task<Item>>> ItemFactoryAsync()
    {
        var uom = DictionaryManager.NewRecord<UnitOfMeasure>();
        uom.Name = "Piece";
        uom.Code = "PCS";
        uom = await DictionaryManager.SaveRecordAsync(uom);

        var group = DictionaryManager.NewRecord<ItemGroup>();
        group.Code = "PRODCOST";
        group.Name = "Производство-себестоимость";
        group = await DictionaryManager.SaveRecordAsync(group);

        return async name =>
        {
            var item = DictionaryManager.NewRecord<Item>();
            item.Name = name;
            item.ItemGroup = group.MetaId;
            item.UnitOfMeasure = uom.MetaId;
            return await DictionaryManager.SaveRecordAsync(item);
        };
    }

    /// <summary>ItemCostFifo адресуется физическим измерением Item — точный срез.</summary>
    private static Task<decimal> FifoAsync(string resource, Guid item)
        => TotalsManager.GetBalanceAsync("ItemCostFifo", resource,
            new Dictionary<string, object?> { ["Item"] = item });

    /// <summary>InventoryValue разрезан ДИНАМИЧЕСКОЙ аналитикой Item — точечного среза
    /// по товару через ITotalsManager нет, поэтому сравнивается сумма по ВСЕМУ
    /// регистру до/после (стенд общий — те же дельты, что в UnitAwareTradeCycleTest).</summary>
    private static async Task<decimal> InventoryValueTotalAsync(string resource)
    {
        decimal sum = 0m;
        foreach (var row in await TotalsManager.QueryBalancesAsync("InventoryValue"))
            if (row.TryGetValue(resource, out var v) && v != null) sum += Convert.ToDecimal(v);
        return sum;
    }

    [IntegrationTest("Выпуск заводит партию изделию средней стоимостью компонентов и не меняет суммарную стоимость запасов")]
    public async Task FinishRollsUpComponentCostToProduct()
    {
        var loc = Db.NewId();
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Изделие-Себест");
        var comp = await newItem("Компонент-Себест");

        // Остаток и партия себестоимости компонента: 20 ед. по 10/ед (200 всего).
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = loc, ["Item"] = comp.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 20m });
        await TotalsManager.PostMovementAsync("ItemCostFifo", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Item"] = comp.MetaId },
            new Dictionary<string, decimal> { ["Quantity"] = 20m, ["Amount"] = 200m });

        var valueBefore = await InventoryValueTotalAsync("Value");
        var qtyBefore = await InventoryValueTotalAsync("Qty");

        var order = await NewOrderAsync(product.MetaId, 5m, loc, comp.MetaId, 10m);
        order.Subtype = ProductionOrder.Subtypes.Finished;
        await DocumentManager.SaveDocumentAsync(order);

        // Изделие: новая партия по средней стоимости потреблённых 10 ед. компонента
        // (200/20×10 = 100), количество партии — выпущенные 5 ед.
        var productQty = await FifoAsync("Quantity", product.MetaId);
        var productAmt = await FifoAsync("Amount", product.MetaId);
        Assert.IsTrue(productQty == 5m, "партия изделия должна нести выпущенное количество, факт {0}", productQty);
        Assert.IsTrue(productAmt == 100m, "партия изделия должна стоить 200/20×10 = 100, факт {0}", productAmt);

        // Компонент: списаны те же 10 ед. по той же цене — из партии осталось 10/100.
        var compQty = await FifoAsync("Quantity", comp.MetaId);
        var compAmt = await FifoAsync("Amount", comp.MetaId);
        Assert.IsTrue(compQty == 10m, "у компонента должно остаться 20-10=10 ед., факт {0}", compQty);
        Assert.IsTrue(compAmt == 100m, "у компонента должно остаться 200-100=100 стоимости, факт {0}", compAmt);

        // InventoryValue: Costing лишь переносит стоимость между товарами —
        // суммарная стоимость запасов не меняется (-100 компонент, +100 изделие).
        var valueDelta = await InventoryValueTotalAsync("Value") - valueBefore;
        var qtyDelta = await InventoryValueTotalAsync("Qty") - qtyBefore;
        Assert.IsTrue(valueDelta == 0m, "перенос стоимости не создаёт и не уничтожает её, дельта {0}", valueDelta);
        Assert.IsTrue(qtyDelta == -5m, "количество регистра: -10 компонент + 5 изделие = -5, факт {0}", qtyDelta);
    }

    [IntegrationTest("Выпуск без стоимостной партии компонента заводит изделию партию с нулевой стоимостью, а не падает")]
    public async Task FinishWithoutCostBasisYieldsZeroCostLot()
    {
        var loc = Db.NewId();
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Изделие-БезПартии");
        var comp = await newItem("Компонент-БезПартии");

        // Остаток есть, а партии себестоимости — нет (как у тестовых остатков,
        // заведённых напрямую движением регистра, без прихода).
        await TotalsManager.PostMovementAsync("Stock", null, DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = loc, ["Item"] = comp.MetaId },
            new Dictionary<string, decimal> { ["Qty"] = 20m });

        var order = await NewOrderAsync(product.MetaId, 5m, loc, comp.MetaId, 10m);
        order.Subtype = ProductionOrder.Subtypes.Finished;
        await DocumentManager.SaveDocumentAsync(order);

        var productQty = await FifoAsync("Quantity", product.MetaId);
        var productAmt = await FifoAsync("Amount", product.MetaId);
        Assert.IsTrue(productQty == 5m, "партия по количеству заводится всегда, факт {0}", productQty);
        Assert.IsTrue(productAmt == 0m, "компонент без стоимостной партии стоит 0, факт {0}", productAmt);
    }

    /// <summary>Черновик заказа с одной строкой компонента (тот же хелпер, что в ProductionFlowTest).</summary>
    private static async Task<ProductionOrder> NewOrderAsync(Guid product, decimal quantity, Guid location, Guid component, decimal qtyRequired)
    {
        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = product;
        order.Quantity = quantity;
        order.Location = location;
        order.Components.Add(new ProductionOrderComponentsTablePartRow { Component = component, QtyRequired = qtyRequired });
        await DocumentManager.SaveDocumentAsync(order);
        return order;
    }
}
