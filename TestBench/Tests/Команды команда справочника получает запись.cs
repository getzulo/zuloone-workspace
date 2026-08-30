// «Ядерные тесты.Команды»: DictionaryCommand типизирована TBWarehouse — хук
// получает ЗАГРУЖЕННУЮ запись (DictionaryManager), сообщение несёт её имя.
//
// Запись готовится типизированным IDictionaryManager. Само исполнение команды
// остаётся на Db: семейства команд MIQS (user/dictionary/document/…) не имеют
// менеджера в платформе — их диспетчер это CommandFamilyService, и харнес лишь
// приводит его CommandResult к виду, удобному тесту.
public partial class TbDictionaryCommandTests
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    [IntegrationTest("Команды: команда справочника получает запись")]
    public async Task DictionaryCommandReceivesRecord()
    {
        // Имя сразу в верхнем регистре: обработчик событий TBWarehouse не тронет его.
        var name = "CMD-WH-" + System.Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();

        var warehouse = DictionaryManager.NewRecord<TBWarehouse>();
        warehouse.Name = name;
        warehouse = await DictionaryManager.SaveRecordAsync(warehouse);

        var commandId = await Db.FindCommandIdAsync("dictionary", "TBWarehouseInfo");
        var result = await Db.ExecuteDictionaryCommandAsync(commandId, warehouse.MetaId);
        Assert.IsTrue(result.Success, "команда должна выполниться: {0}", result.Message ?? "");
        Assert.IsTrue(result.ClientMessages.Contains("warehouse=" + name),
            "сообщение несёт имя записи: {0}", string.Join("; ", result.ClientMessages));
        Log("DictionaryCommand прочитала запись: warehouse=" + name + ".");
    }
}
