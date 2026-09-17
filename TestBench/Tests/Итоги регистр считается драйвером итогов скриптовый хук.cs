// «Ядерные тесты.Итоги»: регистр TBFifoDriven объявлен Standard, но привязан к
// драйверу итогов TBRounding (FifoTotalDriver + скриптовый хук округления).
// Себестоимость расхода обязана прийти из ХУКА драйвера, а не из базового FIFO.
public partial class TbDriverHookTests
{
    [IntegrationTest("Итоги: регистр считается драйвером итогов (скриптовый хук)")]
    public async Task DriverHookComputesCogs()
    {
        var item = Db.NewId();
        var t0 = System.DateTime.UtcNow.AddMinutes(-10);
        // Приход 3 шт за 100 — точная себестоимость единицы 33.3333…
        await Db.PostMovementAsync("TBFifoDriven", t0,
            new Dictionary<string, object?> { ["Item"] = item },
            new Dictionary<string, decimal> { ["Quantity"] = 3m, ["Amount"] = 100m });
        // Расход 1 шт — Amount заменяется COGS, посчитанным драйвером.
        await Db.PostMovementAsync("TBFifoDriven", t0.AddMinutes(1),
            new Dictionary<string, object?> { ["Item"] = item },
            new Dictionary<string, decimal> { ["Quantity"] = -1m, ["Amount"] = 0m });

        var bal = await Db.QueryBalancesAsync("TBFifoDriven", "[Item] = '" + item + "'");
        Assert.AreEqual(1, bal.Count, "одна строка остатка");
        Assert.AreEqual(2m, System.Convert.ToDecimal(bal[0]["Quantity"]), "остаток 2 шт");
        // Хук драйвера: COGS = Round(33.3333…, 2) = 33.33 → остаток стоимости ровно 66.67.
        // Базовый FIFO без хука дал бы 33.3333 → 66.6667.
        Assert.AreEqual(66.67m, System.Convert.ToDecimal(bal[0]["Amount"]),
            "себестоимость списана скриптовым хуком драйвера (округление до 2 знаков)");
        Log("Драйвер итогов посчитал COGS: 100 − 33.33 = 66.67.");
    }
}