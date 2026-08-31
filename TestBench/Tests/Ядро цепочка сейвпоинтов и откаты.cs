// Ядро СУБД-слоя: цепочка сейвпоинтов внутри транзакции кейса. Откат к
// сейвпоинту убирает только более поздние записи, транзакция остаётся живой
// (тот же механизм держит атомарность проведения документов).
//
// Данные пишутся и читаются типизированным IDictionaryManager — тем же путём,
// что и бизнес-код, иначе тест доказывал бы живучесть харнеса, а не транзакции.
// Сами сейвпоинты менеджерами не выражаются (это примитив СУБД-слоя, а не
// бизнес-операция) и осознанно остаются на Db.
public partial class TbKernelSavepointTests
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    private static async Task<Guid> WarehouseAsync(string name)
    {
        var warehouse = DictionaryManager.NewRecord<TBWarehouse>();
        warehouse.Name = name;
        warehouse = await DictionaryManager.SaveRecordAsync(warehouse);
        return warehouse.MetaId;
    }

    [IntegrationTest("Ядро: цепочка сейвпоинтов и откаты")]
    public async Task SavepointChain()
    {
        // Уникальный префикс: посторонние записи не влияют на счётчики.
        var p = ("SP" + System.Guid.NewGuid().ToString("N").Substring(0, 6) + "-").ToUpperInvariant();
        var filter = "[Name] LIKE '" + p + "%'";

        var a = await WarehouseAsync(p + "A");
        await Db.SavepointAsync("KernelSp1");
        var b = await WarehouseAsync(p + "B");
        await Db.SavepointAsync("KernelSp2");
        var c = await WarehouseAsync(p + "C");
        Assert.AreEqual(3, await DictionaryManager.CountAsync<TBWarehouse>(filter), "до откатов видны все три записи");

        await Db.RollbackToSavepointAsync("KernelSp2");
        Assert.IsNull(await DictionaryManager.GetRecordAsync<TBWarehouse>(c), "откат к SP2 удалил C");
        Assert.IsNotNull(await DictionaryManager.GetRecordAsync<TBWarehouse>(b), "B записан до SP2 и пережил откат");

        await Db.RollbackToSavepointAsync("KernelSp1");
        Assert.IsNull(await DictionaryManager.GetRecordAsync<TBWarehouse>(b), "откат к SP1 удалил B");
        Assert.IsNotNull(await DictionaryManager.GetRecordAsync<TBWarehouse>(a), "A записан до SP1 и жив");

        // Транзакция не погибла: после откатов в ней можно продолжать писать.
        var d = await WarehouseAsync(p + "D");
        Assert.IsNotNull(await DictionaryManager.GetRecordAsync<TBWarehouse>(d), "после откатов транзакция принимает новые записи");
        Assert.AreEqual(2, await DictionaryManager.CountAsync<TBWarehouse>(filter), "остались ровно A и D");
        Log("Цепочка SP1→SP2, откаты к SP2 и SP1, запись после отката — всё подтверждено.");
    }
}
