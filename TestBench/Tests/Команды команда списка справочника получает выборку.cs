// «Ядерные тесты.Команды»: DictionaryListCommand типизирована TBItem — хук
// получает всю выборку загруженными записями и считает её.
//
// Выборка готовится типизированно через IDictionaryManager (это partial-скрипт,
// поэтому ZuloOne.Managers и ZuloOne.Runtime.Generated приходят глобальными
// using'ами от фреймворка). На харнессе остаётся только ЗАПУСК команды:
// платформа не публикует ICommandManager, исполнять семейства команд умеет
// только тест-харнесс.
public partial class TbDictionaryListCommandTests
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    [IntegrationTest("Команды: команда списка справочника получает выборку")]
    public async Task DictionaryListCommandReceivesSelection()
    {
        var warehouse = DictionaryManager.NewRecord<TBWarehouse>();
        warehouse.Name = "CMD-LIST-WH";
        warehouse = await DictionaryManager.SaveRecordAsync(warehouse);

        var ids = new List<System.Guid>();
        for (var i = 0; i < 3; i++)
        {
            var item = DictionaryManager.NewRecord<TBItem>();
            item.WarehouseID = warehouse.MetaId;
            item = await DictionaryManager.SaveRecordAsync(item);
            ids.Add(item.MetaId);
        }

        var commandId = await Db.FindCommandIdAsync("dictionarylist", "TBItemTally");
        var result = await Db.ExecuteDictionaryListCommandAsync(commandId, ids);
        Assert.IsTrue(result.Success, "команда должна выполниться: {0}", result.Message ?? "");
        Assert.IsTrue(result.ClientMessages.Contains("count=3"),
            "команда сосчитала все переданные записи: {0}", string.Join("; ", result.ClientMessages));
        Log("DictionaryListCommand получила выборку: count=3.");
    }
}
