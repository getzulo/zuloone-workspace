// «Ядерные тесты.Итоги»: регистр TBFifoDriven объявлен Standard, но привязан к
// драйверу итогов TBRounding (FifoTotalDriver + скриптовый хук округления).
// Себестоимость расхода обязана прийти из ХУКА драйвера, а не из базового FIFO.
//
// Регистр читается и пишется через ITotalsManager — ту же дверь, что и бизнес-код.
// Имена регистра/ресурсов остаются строками: регистр не порождает класс, поэтому
// типизировать здесь нечего; типизирован ВЫЗОВ, а не имя.
public partial class TbDriverHookTests
{
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("Итоги: регистр считается драйвером итогов (скриптовый хук)")]
    public async Task DriverHookComputesCogs()
    {
        var item = Db.NewId();
        var t0 = System.DateTime.UtcNow.AddMinutes(-10);

        // Регистр пуст до прихода — иначе «остаток 2 шт» ниже ничего не доказывает:
        // он сошёлся бы и на чужих движениях, попавших в тот же срез.
        Assert.AreEqual(0m, await TotalsManager.GetBalanceAsync("TBFifoDriven", "Quantity",
            new Dictionary<string, object?> { ["Item"] = item }),
            "срез номенклатуры пуст до первого движения");

        // Приход 3 шт за 100 — точная себестоимость единицы 33.3333…
        // documentMetaId = null: движение сознательно вне документа (ITotalsManager
        // требует назвать владельца явно, а не умалчивать его).
        await TotalsManager.PostMovementAsync("TBFifoDriven", null, t0,
            new Dictionary<string, object?> { ["Item"] = item },
            new Dictionary<string, decimal> { ["Quantity"] = 3m, ["Amount"] = 100m });
        // Расход 1 шт — Amount заменяется COGS, посчитанным драйвером.
        await TotalsManager.PostMovementAsync("TBFifoDriven", null, t0.AddMinutes(1),
            new Dictionary<string, object?> { ["Item"] = item },
            new Dictionary<string, decimal> { ["Quantity"] = -1m, ["Amount"] = 0m });

        var bal = await TotalsManager.QueryBalancesAsync("TBFifoDriven", "[Item] = '" + item + "'");
        Assert.AreEqual(1, bal.Count, "одна строка остатка");
        Assert.AreEqual(2m, System.Convert.ToDecimal(bal[0]["Quantity"]), "остаток 2 шт");
        // Хук драйвера: COGS = Round(33.3333…, 2) = 33.33 → остаток стоимости ровно 66.67.
        // Базовый FIFO без хука дал бы 33.3333 → 66.6667.
        Assert.AreEqual(66.67m, System.Convert.ToDecimal(bal[0]["Amount"]),
            "себестоимость списана скриптовым хуком драйвера (округление до 2 знаков)");
        Log("Драйвер итогов посчитал COGS: 100 − 33.33 = 66.67.");
    }
}
