// MIQS-style референс: скалярное поле TBItem.WarehouseID (Reference EDT) даёт
// в ГЕНЕРЁННОМ классе companion-инстанс Warehouse (запись TBWarehouse),
// разрешаемый лениво по id; смена id инвалидирует закэшированный инстанс.
//
// Данные заводятся типизированно через IDictionaryManager (NewRecord → поля →
// SaveRecordAsync), как это делает прикладной код: если companion работает
// только над строкой, положенной сырым InsertAsync, тест ничего не доказывает
// про рабочий путь.
public partial class TbReferenceCompanionTests
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    [IntegrationTest("Справочники: референс-поле даёт инстанс записи")]
    public async Task CompanionResolvesInstance()
    {
        var warehouse = DictionaryManager.NewRecord<TBWarehouse>();
        warehouse.Name = "СКЛАД ССЫЛОК";
        warehouse = await DictionaryManager.SaveRecordAsync(warehouse);

        var newItem = DictionaryManager.NewRecord<TBItem>();
        newItem.WarehouseID = warehouse.MetaId;
        newItem = await DictionaryManager.SaveRecordAsync(newItem);

        // Перечитываем из базы: companion должен разрешаться на записи, ПРИШЕДШЕЙ
        // из хранилища, а не только на той, что осталась в памяти после save.
        var item = await DictionaryManager.GetRecordAsync<TBItem>(newItem.MetaId);
        Assert.IsNotNull(item, "TBItem загружается как генерённый класс");
        Assert.AreEqual(warehouse.MetaId, item!.WarehouseID, "скалярное поле хранит id склада");
        Assert.IsNotNull(item.Warehouse, "companion-свойство Warehouse возвращает инстанс записи");
        Assert.AreEqual("СКЛАД ССЫЛОК", item.Warehouse!.Name, "инстанс загружен по WarehouseID");
        Log("Companion разрезолвлен: WarehouseID → '" + item.Warehouse.Name + "'.");

        var other = DictionaryManager.NewRecord<TBWarehouse>();
        other.Name = "ДРУГОЙ СКЛАД";
        other = await DictionaryManager.SaveRecordAsync(other);

        item.WarehouseID = other.MetaId;
        Assert.AreEqual("ДРУГОЙ СКЛАД", item.Warehouse!.Name, "смена id инвалидирует кэш companion-инстанса");

        item.WarehouseID = System.Guid.Empty;
        Assert.IsNull(item.Warehouse, "пустой id даёт null-инстанс");
        Log("Инвалидация и null-семантика подтверждены.");
    }
}
