// «Ядерные тесты.Итоги»: эффективный движок регистра задаёт ДРАЙВЕР, а не
// строка RegisterEngineType. TBFifoDriven объявлен Standard — но перерасход
// отклоняется FIFO-контролем слоёв, унаследованным от базы драйвера.
//
// Движения пишутся и читаются через ITotalsManager — ту же дверь, что и
// прикладной код (обработчики событий зовут TotalsManager.GetBalanceAsync),
// а не через дата-образный харнесс.
public partial class TbDriverEngineTests
{
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("Итоги: драйвер задаёт эффективный движок регистра")]
    public async Task DriverDefinesEffectiveEngine()
    {
        var item = Db.NewId();
        var key = new Dictionary<string, object?> { ["Item"] = item };
        var t0 = System.DateTime.UtcNow.AddMinutes(-10);

        await TotalsManager.PostMovementAsync("TBFifoDriven", null, t0,
            key,
            new Dictionary<string, decimal> { ["Quantity"] = 2m, ["Amount"] = 20m });

        // Состояние ДО отказа: слой на 2 шт действительно лёг. Без этой проверки
        // тест зелен и тогда, когда приход не прошёл вовсе — «отклонён» означало
        // бы лишь «нечего расходовать».
        Assert.AreEqual(2m, await TotalsManager.GetBalanceAsync("TBFifoDriven", "Quantity", key),
            "приход 2 шт должен лечь в остаток регистра");

        // Standard-движок принял бы уход в минус; FIFO из драйвера — отклоняет.
        // После отказа к БД не обращаемся: исключение рушит окружающую транзакцию
        // раннера, и любой следующий запрос упал бы вместо самой проверки.
        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => TotalsManager.PostMovementAsync("TBFifoDriven", null, t0.AddMinutes(1),
                key,
                new Dictionary<string, decimal> { ["Quantity"] = -5m, ["Amount"] = 0m }),
            "расход 5 шт при остатке 2 шт должен быть отклонён FIFO-движком драйвера");
        Assert.IsTrue(ex.Message.Contains("Insufficient"), "движок сообщает о нехватке слоёв: {0}", ex.Message);
        Log("Регистр Standard, движок из драйвера — FIFO: перерасход отклонён.");
    }
}
