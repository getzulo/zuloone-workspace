// Ядро СУБД-слоя: цепочка сейвпоинтов внутри транзакции кейса. Откат к
// сейвпоинту убирает только более поздние записи, транзакция остаётся живой
// (тот же механизм держит атомарность проведения документов).
public partial class TbKernelSavepointTests
{
    [IntegrationTest("Ядро: цепочка сейвпоинтов и откаты")]
    public async Task SavepointChain()
    {
        // Уникальный префикс: посторонние записи не влияют на счётчики.
        var p = ("SP" + System.Guid.NewGuid().ToString("N").Substring(0, 6) + "-").ToUpperInvariant();
        var filter = "[Name] LIKE '" + p + "%'";

        var a = await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = p + "A" });
        await Db.SavepointAsync("KernelSp1");
        var b = await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = p + "B" });
        await Db.SavepointAsync("KernelSp2");
        var c = await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = p + "C" });
        Assert.AreEqual(3, await Db.CountAsync("TBWarehouse", filter), "до откатов видны все три записи");

        await Db.RollbackToSavepointAsync("KernelSp2");
        Assert.IsNull(await Db.GetAsync("TBWarehouse", c), "откат к SP2 удалил C");
        Assert.IsNotNull(await Db.GetAsync("TBWarehouse", b), "B записан до SP2 и пережил откат");

        await Db.RollbackToSavepointAsync("KernelSp1");
        Assert.IsNull(await Db.GetAsync("TBWarehouse", b), "откат к SP1 удалил B");
        Assert.IsNotNull(await Db.GetAsync("TBWarehouse", a), "A записан до SP1 и жив");

        // Транзакция не погибла: после откатов в ней можно продолжать писать.
        var d = await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = p + "D" });
        Assert.IsNotNull(await Db.GetAsync("TBWarehouse", d), "после откатов транзакция принимает новые записи");
        Assert.AreEqual(2, await Db.CountAsync("TBWarehouse", filter), "остались ровно A и D");
        Log("Цепочка SP1→SP2, откаты к SP2 и SP1, запись после отката — всё подтверждено.");
    }
}