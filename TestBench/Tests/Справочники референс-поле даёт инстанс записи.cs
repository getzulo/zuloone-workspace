// MIQS-style референс: скалярное поле TBItem.WarehouseID (Reference EDT) даёт
// в ГЕНЕРЁННОМ классе companion-инстанс Warehouse (запись TBWarehouse),
// разрешаемый лениво по id; смена id инвалидирует закэшированный инстанс.
public partial class TbReferenceCompanionTests
{
    [IntegrationTest("Справочники: референс-поле даёт инстанс записи")]
    public async Task CompanionResolvesInstance()
    {
        var warehouseId = await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = "СКЛАД ССЫЛОК" });
        var itemId = await Db.InsertAsync("TBItem", new Dictionary<string, object?> { ["WarehouseID"] = warehouseId });

        var item = await Db.GetRecordAsync<TBItem>(itemId);
        Assert.IsNotNull(item, "TBItem загружается как генерённый класс");
        Assert.AreEqual(warehouseId, item!.WarehouseID, "скалярное поле хранит id склада");
        Assert.IsNotNull(item.Warehouse, "companion-свойство Warehouse возвращает инстанс записи");
        Assert.AreEqual("СКЛАД ССЫЛОК", item.Warehouse!.Name, "инстанс загружен по WarehouseID");
        Log("Companion разрезолвлен: WarehouseID → '" + item.Warehouse.Name + "'.");

        var otherId = await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = "ДРУГОЙ СКЛАД" });
        item.WarehouseID = otherId;
        Assert.AreEqual("ДРУГОЙ СКЛАД", item.Warehouse!.Name, "смена id инвалидирует кэш companion-инстанса");

        item.WarehouseID = System.Guid.Empty;
        Assert.IsNull(item.Warehouse, "пустой id даёт null-инстанс");
        Log("Инвалидация и null-семантика подтверждены.");
    }
}