using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Покрытие команды «Развернуть спецификацию»: на черновике она наполняет
// табличную часть Components потребностью из BOM, а на подтипе Finished
// платформа её не пускает (команда привязана только к Draft).
public class ExpandBomCommandTest : IntegrationTestScriptBase
{
    private async Task<Func<string, Task<object>>> ItemFactoryAsync()
    {
        var uom = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?> { ["Name"] = "Piece", ["Code"] = "PCS" });
        var group = await Db.InsertAsync("ItemGroup", new Dictionary<string, object?> { ["Code"] = "MAT", ["Name"] = "Materials" });
        return async name => await Db.InsertAsync("Item",
            new Dictionary<string, object?> { ["Name"] = name, ["ItemGroup"] = group, ["UnitOfMeasure"] = uom });
    }

    private async Task<object> NewLocationAsync()
    {
        // Подразделение обязано принадлежать юрлицу (проверяется событием), а
        // юрлицо — стране и валюте: цепочку приходится сеять целиком.
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?> { ["Name"] = "Euro", ["Code"] = "EUR", ["Symbol"] = "€" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?> { ["Name"] = "Germany", ["CodeISO2"] = "DE", ["CodeISO3"] = "DEU", ["PhoneCode"] = "49" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME GmbH", ["RegistrationNumber"] = "REG-BOM-1", ["Country"] = country, ["Currency"] = currency });
        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?> { ["Code"] = "PRD", ["Name"] = "Production" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?> { ["Name"] = "Цех", ["LegalEntity"] = le, ["DivisionType"] = dt });
        // Склад переименован: Warehouse → Store, WarehouseLocation → StoreCell
        // (metaId сохранены, поэтому ссылки живы), LocationType → StoreCellType.
        // Структура стала глубже: ячейка обязана лежать в зоне и знать свои
        // координаты (стеллаж/полка/линия/ячейка).
        // Иерархия теперь трёхуровневая: Store → StoreZone → StoreCell, ячейка
        // знает только свою зону; тип ячейки — Type.
        var store = await Db.InsertAsync("Store", new Dictionary<string, object?>
            { ["Name"] = "Склад цеха", ["Division"] = div, ["IsSimple"] = true });
        var zone = await Db.InsertAsync("StoreZone", new Dictionary<string, object?>
            { ["Name"] = "Зона цеха", ["Store"] = store, ["IsBarcodeTracking"] = false });
        var ct = await Db.InsertAsync("StoreCellType", new Dictionary<string, object?>
            { ["Code"] = $"PRD-{Db.NewId():N}"[..12], ["Name"] = "Production" });
        return await Db.InsertAsync("StoreCell", new Dictionary<string, object?>
        {
            ["StoreZone"] = zone, ["Type"] = ct, ["Name"] = "P-01",
            ["RackNumber"] = 1, ["ShelfNumber"] = 1, ["LineNumber"] = 1, ["CellNumber"] = 1,
        });
    }

    [IntegrationTest("Команда разворачивает спецификацию в компоненты заказа")]
    public async Task CommandFillsComponents()
    {
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Бутерброд");
        var c1 = await newItem("Колбаса");
        var c2 = await newItem("Булка");
        var loc = await NewLocationAsync();

        var bom = await Db.InsertAsync("BillOfMaterials",
            new Dictionary<string, object?> { ["Name"] = "BOM-бутерброд", ["Product"] = product, ["OutputQty"] = 1m });
        await Db.InsertAsync("BomComponent", new Dictionary<string, object?> { ["Bom"] = bom, ["Component"] = c1, ["QtyPer"] = 2m });
        await Db.InsertAsync("BomComponent", new Dictionary<string, object?> { ["Bom"] = bom, ["Component"] = c2, ["QtyPer"] = 1m });

        // Заказ создаётся БЕЗ строк — их и должна проставить команда.
        // Подтип задаётся явно: команда привязана к Draft, а без указания
        // документ создаётся с пустым подтипом и привязка не совпадает.
        var order = await Db.CreateDocumentAsync("ProductionOrder",
            new Dictionary<string, object?> { ["Product"] = product, ["Quantity"] = 10m, ["Location"] = loc },
            null, subtype: "Draft");

        var commandId = await Db.FindCommandIdAsync("document", "ExpandBom");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, (Guid)order);
        Assert.IsTrue(run.Success, "команда должна выполниться: {0}", run.Message ?? "");

        var rows = await Db.QueryAsync("TP_ProductionOrderComponents", $"OwnerMetaId = '{order}'");
        Assert.IsTrue(rows.Count == 2, "развёрнуто 2 строки, факт {0}", rows.Count);

        decimal Qty(object item) => rows
            .Where(r => Convert.ToString(r["Component"]) == Convert.ToString(item))
            .Select(r => Convert.ToDecimal(r["QtyRequired"])).FirstOrDefault();

        Assert.IsTrue(Qty(c1) == 20m, "Колбаса: 2 × 10 = 20, факт {0}", Qty(c1));
        Assert.IsTrue(Qty(c2) == 10m, "Булка: 1 × 10 = 10, факт {0}", Qty(c2));
    }

    [IntegrationTest("Команда недоступна вне подтипа Draft")]
    public async Task CommandBoundToDraftOnly()
    {
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Изделие-Б");
        var comp = await newItem("Компонент-Б");
        var loc = await NewLocationAsync();

        var bom = await Db.InsertAsync("BillOfMaterials",
            new Dictionary<string, object?> { ["Name"] = "BOM-Б", ["Product"] = product, ["OutputQty"] = 1m });
        await Db.InsertAsync("BomComponent", new Dictionary<string, object?> { ["Bom"] = bom, ["Component"] = comp, ["QtyPer"] = 1m });

        // Заказ со строкой и остатком — чтобы перевод в Finished прошёл.
        var order = await Db.CreateDocumentAsync("ProductionOrder",
            new Dictionary<string, object?> { ["Product"] = product, ["Quantity"] = 1m, ["Location"] = loc },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Components"] = new[] { new Dictionary<string, object?> { ["Component"] = comp, ["QtyRequired"] = 1m } } });

        await Db.PostMovementAsync("Stock", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = loc, ["Item"] = comp },
            new Dictionary<string, decimal> { ["Qty"] = 5m });
        await Db.ChangeSubtypeAsync("ProductionOrder", order, "Finished");

        var commandId = await Db.FindCommandIdAsync("document", "ExpandBom");
        var rejected = await Db.ExecuteDocumentCommandAsync(commandId, (Guid)order);
        Assert.IsFalse(rejected.Success, "на подтипе Finished команда обязана быть отклонена привязкой");
    }

    [IntegrationTest("Рецепт нормируется на партию и переводится в складскую единицу")]
    public async Task BatchAndUnitAreHonoured()
    {
        // Сценарий-бутерброд: рецепт даёт 10 бутербродов и требует 20 ГРАММОВ
        // колбасы на партию, а колбаса хранится в КИЛОГРАММАХ. Правильный ответ на
        // заказ в 10 штук — 0.020 кг. Две ошибки, которые тест обязан ловить:
        // умножить на заказ вместо деления на выход (было бы 200) и посчитать
        // граммы килограммами (было бы 20).
        var kg = await Db.InsertAsync("UnitOfMeasure",
            new Dictionary<string, object?> { ["Name"] = "Kilogram", ["Code"] = "KG", ["DecimalPlaces"] = 3 });
        var g = await Db.InsertAsync("UnitOfMeasure",
            new Dictionary<string, object?> { ["Name"] = "Gram", ["Code"] = "G", ["DecimalPlaces"] = 0 });
        var pcs = await Db.InsertAsync("UnitOfMeasure",
            new Dictionary<string, object?> { ["Name"] = "Piece", ["Code"] = "PCS", ["DecimalPlaces"] = 0 });
        await Db.InsertAsync("UnitConversion",
            new Dictionary<string, object?> { ["FromUnit"] = kg, ["ToUnit"] = g, ["Factor"] = 1000m });

        var group = await Db.InsertAsync("ItemGroup", new Dictionary<string, object?> { ["Code"] = "MAT", ["Name"] = "Materials" });
        var product = await Db.InsertAsync("Item",
            new Dictionary<string, object?> { ["Name"] = "Бутерброд", ["ItemGroup"] = group, ["UnitOfMeasure"] = pcs });
        var sausage = await Db.InsertAsync("Item",
            new Dictionary<string, object?> { ["Name"] = "Колбаса", ["ItemGroup"] = group, ["UnitOfMeasure"] = kg });
        var bun = await Db.InsertAsync("Item",
            new Dictionary<string, object?> { ["Name"] = "Булка", ["ItemGroup"] = group, ["UnitOfMeasure"] = pcs });

        var bom = await Db.InsertAsync("BillOfMaterials",
            new Dictionary<string, object?> { ["Name"] = "BOM-партия", ["Product"] = product, ["OutputQty"] = 10m });
        await Db.InsertAsync("BomComponent", new Dictionary<string, object?>
            { ["Bom"] = bom, ["Component"] = sausage, ["QtyPer"] = 20m, ["Unit"] = g });
        await Db.InsertAsync("BomComponent", new Dictionary<string, object?>
            { ["Bom"] = bom, ["Component"] = bun, ["QtyPer"] = 10m, ["Unit"] = pcs });

        var bomService = GetService<IBomService>();
        var need = await bomService.ExpandByProductAsync((Guid)product, 10m);

        Assert.IsTrue(need[(Guid)sausage] == 0.020m,
            "20 г на партию из 10 при заказе 10 = 0.020 кг, факт {0}", need[(Guid)sausage]);
        Assert.IsTrue(need[(Guid)bun] == 10m,
            "булки уже в штуках: 10 на партию из 10 при заказе 10 = 10, факт {0}", need[(Guid)bun]);

        // Удвоенный заказ — удвоенная потребность: нормировка линейна.
        var doubled = await bomService.ExpandByProductAsync((Guid)product, 20m);
        Assert.IsTrue(doubled[(Guid)sausage] == 0.040m,
            "на 20 бутербродов нужно 0.040 кг, факт {0}", doubled[(Guid)sausage]);
    }

    [IntegrationTest("Настройка AutoExpandBom разворачивает спецификацию сама")]
    public async Task AutoExpandFillsOnCreate()
    {
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Бутерброд");
        var c1 = await newItem("Колбаса");
        var loc = await NewLocationAsync();

        var bom = await Db.InsertAsync("BillOfMaterials",
            new Dictionary<string, object?> { ["Name"] = "BOM-авто", ["Product"] = product, ["OutputQty"] = 1m });
        await Db.InsertAsync("BomComponent",
            new Dictionary<string, object?> { ["Bom"] = bom, ["Component"] = c1, ["QtyPer"] = 3m });

        // Настройки модуля — одиночный справочник; без записи настройка выключена,
        // поэтому остальные тесты создают заказы без автоподстановки.
        await Db.InsertAsync("ProductionSettings", new Dictionary<string, object?> { ["AutoExpandBom"] = true });

        var order = await Db.CreateDocumentAsync("ProductionOrder",
            new Dictionary<string, object?> { ["Product"] = product, ["Quantity"] = 4m, ["Location"] = loc },
            null, subtype: "Draft");

        // Команду НЕ вызываем: строки должно проставить событие создания. Это же и
        // проверка платформенного фикса — чтение второго справочника в after-insert.
        var rows = await Db.QueryAsync("TP_ProductionOrderComponents", $"OwnerMetaId = '{order}'");
        Assert.IsTrue(rows.Count == 1, "автоподстановка должна дать 1 строку, факт {0}", rows.Count);
        Assert.IsTrue(Convert.ToDecimal(rows[0]["QtyRequired"]) == 12m,
            "3 на изделие × 4 = 12, факт {0}", rows[0]["QtyRequired"]);
    }

    [IntegrationTest("Без записи настроек автоподстановка не срабатывает")]
    public async Task NoSettingsNoAutoExpand()
    {
        var newItem = await ItemFactoryAsync();
        var product = await newItem("Бутерброд");
        var c1 = await newItem("Колбаса");
        var loc = await NewLocationAsync();

        var bom = await Db.InsertAsync("BillOfMaterials",
            new Dictionary<string, object?> { ["Name"] = "BOM-ручной", ["Product"] = product, ["OutputQty"] = 1m });
        await Db.InsertAsync("BomComponent",
            new Dictionary<string, object?> { ["Bom"] = bom, ["Component"] = c1, ["QtyPer"] = 3m });

        var order = await Db.CreateDocumentAsync("ProductionOrder",
            new Dictionary<string, object?> { ["Product"] = product, ["Quantity"] = 4m, ["Location"] = loc },
            null, subtype: "Draft");

        var rows = await Db.QueryAsync("TP_ProductionOrderComponents", $"OwnerMetaId = '{order}'");
        Assert.IsTrue(rows.Count == 0, "без настройки строки не подставляются, факт {0}", rows.Count);
    }
}
