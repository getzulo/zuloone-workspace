// Ядро СУБД-слоя: транзакционная изоляция. Запись видна ВНУТРИ транзакции
// кейса и не видна снаружи (READPAST пропускает незакоммиченные строки) —
// а значит откат раннера действительно не оставляет следов.
//
// Запись создаётся типизированным IDictionaryManager (та же дверь, что у
// бизнес-кода), а вот «сколько строк ЗАКОММИЧЕНО» менеджерами не выражается —
// у них нет чтения вне объемлющей транзакции, и это ровно то, что здесь
// проверяется. Поэтому CountCommittedAsync остаётся на Db осознанно.
public partial class TbKernelIsolationTests
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    [IntegrationTest("Ядро: незакоммиченное невидимо вне транзакции")]
    public async Task UncommittedInvisibleOutside()
    {
        var probe = "ISO-" + System.Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant();
        var filter = "[Name] = '" + probe + "'";

        Assert.AreEqual(0, await DictionaryManager.CountAsync<TBWarehouse>(filter), "до вставки записи нет нигде");

        var warehouse = DictionaryManager.NewRecord<TBWarehouse>();
        warehouse.Name = probe;
        await DictionaryManager.SaveRecordAsync(warehouse);

        Assert.AreEqual(1, await DictionaryManager.CountAsync<TBWarehouse>(filter), "внутри транзакции запись видна");
        // Нет менеджерного эквивалента: чтение ВНЕ объемлющей транзакции — свойство
        // хранилища, а не бизнес-слоя, и именно оно здесь под тестом.
        Assert.AreEqual(0, await Db.CountCommittedAsync("TBWarehouse", filter), "снаружи транзакции записи НЕТ — она не закоммичена");
        Log("Изоляция подтверждена: внутри 1, снаружи (committed) 0.");
    }
}
