// «Ядерные тесты.Итоги»: эффективный движок регистра задаёт ДРАЙВЕР, а не
// строка RegisterEngineType. TBFifoDriven объявлен Standard — но перерасход
// отклоняется FIFO-контролем слоёв, унаследованным от базы драйвера.
public partial class TbDriverEngineTests
{
    [IntegrationTest("Итоги: драйвер задаёт эффективный движок регистра")]
    public async Task DriverDefinesEffectiveEngine()
    {
        var item = Db.NewId();
        var t0 = System.DateTime.UtcNow.AddMinutes(-10);
        await Db.PostMovementAsync("TBFifoDriven", t0,
            new Dictionary<string, object?> { ["Item"] = item },
            new Dictionary<string, decimal> { ["Quantity"] = 2m, ["Amount"] = 20m });

        // Standard-движок принял бы уход в минус; FIFO из драйвера — отклоняет.
        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => Db.PostMovementAsync("TBFifoDriven", t0.AddMinutes(1),
                new Dictionary<string, object?> { ["Item"] = item },
                new Dictionary<string, decimal> { ["Quantity"] = -5m, ["Amount"] = 0m }),
            "расход 5 шт при остатке 2 шт должен быть отклонён FIFO-движком драйвера");
        Assert.IsTrue(ex.Message.Contains("Insufficient"), "движок сообщает о нехватке слоёв: {0}", ex.Message);
        Log("Регистр Standard, движок из драйвера — FIFO: перерасход отклонён.");
    }
}