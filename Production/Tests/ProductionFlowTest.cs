using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

public class ProductionFlowTest : IntegrationTestScriptBase
{
    // Item требует ItemGroup и UnitOfMeasure — заводим их и фабрику номенклатуры.
    private async Task<Func<string, Task<Guid>>> ItemFactoryAsync()
    {
        var uom = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?> { ["Name"] = "Piece", ["Code"] = "PCS" });
        var group = await Db.InsertAsync("ItemGroup", new Dictionary<string, object?> { ["Code"] = "PROD", ["Name"] = "Производство" });
        return async name => await Db.InsertAsync("Item",
            new Dictionary<string, object?> { ["Name"] = name, ["ItemGroup"] = group, ["UnitOfMeasure"] = uom });
    }

    [IntegrationTest("Сервис разворачивает BOM в потребность компонентов")]
    public async Task BomServiceExpands()
    {
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Изделие-А");
        var c1 = await newItem("Компонент-1");
        var c2 = await newItem("Компонент-2");

        var bom = await Db.InsertAsync("BillOfMaterials",
            new Dictionary<string, object?> { ["Name"] = "BOM-А", ["Product"] = product, ["OutputQty"] = 1m });
        await Db.InsertAsync("BomComponent", new Dictionary<string, object?> { ["Bom"] = bom, ["Component"] = c1, ["QtyPer"] = 2m });
        await Db.InsertAsync("BomComponent", new Dictionary<string, object?> { ["Bom"] = bom, ["Component"] = c2, ["QtyPer"] = 3m });

        var need = await GetService<IBomService>().ExpandByProductAsync((Guid)product, 5m);
        Assert.IsTrue(need.Count == 2, "две позиции потребности, факт {0}", need.Count);
        Assert.IsTrue(need[(Guid)c1] == 10m, "Компонент-1 = 2×5 = 10, факт {0}", need.TryGetValue((Guid)c1, out var v1) ? v1 : -1m);
        Assert.IsTrue(need[(Guid)c2] == 15m, "Компонент-2 = 3×5 = 15, факт {0}", need.TryGetValue((Guid)c2, out var v2) ? v2 : -1m);
    }

    [IntegrationTest("Выпуск списывает компоненты и приходует изделие")]
    public async Task FinishConsumesAndProduces()
    {
        var loc = Db.NewId();
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Изделие-Б");
        var comp = await newItem("Компонент-Б");

        // Заводим 20 ед. компонента на ячейку.
        await Db.PostMovementAsync("Stock", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = loc, ["Item"] = comp },
            new Dictionary<string, decimal> { ["Qty"] = 20m });

        var order = await Db.CreateDocumentAsync("ProductionOrder",
            new Dictionary<string, object?> { ["Product"] = product, ["Quantity"] = 5m, ["Location"] = loc },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Components"] = new[] { new Dictionary<string, object?> { ["Component"] = comp, ["QtyRequired"] = 10m } } });
        await Db.ChangeSubtypeAsync("ProductionOrder", order, "Finished");

        // Stock — односторонний регистр с физическими измерениями: остаток по (ячейка, товар)
        // Компонент: 20 заведено − 10 списано = 10; изделие: выпуск +5.
        decimal onHandComp = 0m, onHandProduct = 0m;
        foreach (var r in await Db.QueryBalancesAsync("Stock", "[Cell] = '" + loc + "' AND [Item] = '" + comp + "'"))
            onHandComp += Convert.ToDecimal(r["Qty"]);
        foreach (var r in await Db.QueryBalancesAsync("Stock", "[Cell] = '" + loc + "' AND [Item] = '" + product + "'"))
            onHandProduct += Convert.ToDecimal(r["Qty"]);
        Assert.IsTrue(onHandComp == 10m, "компонент 20 − 10 = 10, факт {0}", onHandComp);
        Assert.IsTrue(onHandProduct == 5m, "изделие выпущено +5, факт {0}", onHandProduct);
    }

    [IntegrationTest("Нехватка компонента отклоняет выпуск (проверка в событии)")]
    public async Task ShortageRejected()
    {
        var loc = Db.NewId();
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Изделие-В");
        var comp = await newItem("Компонент-В");

        await Db.PostMovementAsync("Stock", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = loc, ["Item"] = comp },
            new Dictionary<string, decimal> { ["Qty"] = 5m });

        var order = await Db.CreateDocumentAsync("ProductionOrder",
            new Dictionary<string, object?> { ["Product"] = product, ["Quantity"] = 3m, ["Location"] = loc },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Components"] = new[] { new Dictionary<string, object?> { ["Component"] = comp, ["QtyRequired"] = 10m } } });

        var rejected = false;
        try { await Db.ChangeSubtypeAsync("ProductionOrder", order, "Finished"); }
        catch (Exception) { rejected = true; }
        Assert.IsTrue(rejected, "выпуск при нехватке компонента (нужно 10, есть 5) должен быть отклонён");
    }

    [IntegrationTest("Выпуск без компонентов отклоняется событием")]
    public async Task EmptyComponentsRejected()
    {
        var loc = Db.NewId();
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Изделие-Г");

        var order = await Db.CreateDocumentAsync("ProductionOrder",
            new Dictionary<string, object?> { ["Product"] = product, ["Quantity"] = 1m, ["Location"] = loc },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Components"] = Array.Empty<IDictionary<string, object?>>() });

        var rejected = false;
        try { await Db.ChangeSubtypeAsync("ProductionOrder", order, "Finished"); }
        catch (Exception) { rejected = true; }
        Assert.IsTrue(rejected, "выпуск без компонентов должен быть отклонён событием");
    }
}
