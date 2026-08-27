// «Ядерные тесты.Команды»: DictionaryListCommand типизирована TBItem — хук
// получает всю выборку загруженными записями и считает её.
public partial class TbDictionaryListCommandTests
{
    [IntegrationTest("Команды: команда списка справочника получает выборку")]
    public async Task DictionaryListCommandReceivesSelection()
    {
        var warehouseId = await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = "CMD-LIST-WH" });
        var ids = new List<System.Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add(await Db.InsertAsync("TBItem", new Dictionary<string, object?> { ["WarehouseID"] = warehouseId }));
        }

        var commandId = await Db.FindCommandIdAsync("dictionarylist", "TBItemTally");
        var result = await Db.ExecuteDictionaryListCommandAsync(commandId, ids);
        Assert.IsTrue(result.Success, "команда должна выполниться: {0}", result.Message ?? "");
        Assert.IsTrue(result.ClientMessages.Contains("count=3"),
            "команда сосчитала все переданные записи: {0}", string.Join("; ", result.ClientMessages));
        Log("DictionaryListCommand получила выборку: count=3.");
    }
}