// «Ядерные тесты.Команды»: DictionaryCommand типизирована TBWarehouse — хук
// получает ЗАГРУЖЕННУЮ запись (DictionaryManager), сообщение несёт её имя.
public partial class TbDictionaryCommandTests
{
    [IntegrationTest("Команды: команда справочника получает запись")]
    public async Task DictionaryCommandReceivesRecord()
    {
        // Имя сразу в верхнем регистре: обработчик событий TBWarehouse не тронет его.
        var name = "CMD-WH-" + System.Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        var recordId = await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = name });

        var commandId = await Db.FindCommandIdAsync("dictionary", "TBWarehouseInfo");
        var result = await Db.ExecuteDictionaryCommandAsync(commandId, recordId);
        Assert.IsTrue(result.Success, "команда должна выполниться: {0}", result.Message ?? "");
        Assert.IsTrue(result.ClientMessages.Contains("warehouse=" + name),
            "сообщение несёт имя записи: {0}", string.Join("; ", result.ClientMessages));
        Log("DictionaryCommand прочитала запись: warehouse=" + name + ".");
    }
}