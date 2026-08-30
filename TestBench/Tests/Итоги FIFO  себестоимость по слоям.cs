using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// «Ядерные тесты. Итоги»: FIFO-движок списывает себестоимость по слоям в порядке дат.
// Слой 1: 10 шт по 100 (1000). Слой 2: 10 шт по 200 (2000). Расход 15 шт →
// COGS = 10*100 + 5*200 = 2000; остаток 5 шт стоимостью 1000.
public class TotalsFifoCogsTest : IntegrationTestScriptBase
{
    [IntegrationTest("FIFO: себестоимость по слоям")]
    public async Task OutcomeCostIsComputedAcrossLayers()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();
        var t0 = DateTime.UtcNow.AddMinutes(-10);

        var receipt1 = await CreateDocAsync(warehouse, item, 10m, 1000m, t0, "Receipt");
        await Db.ChangeStatusAsync("TBStockDoc", receipt1, "Posted");

        var receipt2 = await CreateDocAsync(warehouse, item, 10m, 2000m, t0.AddMinutes(1), "Receipt");
        await Db.ChangeStatusAsync("TBStockDoc", receipt2, "Posted");

        var issue = await CreateDocAsync(warehouse, item, 15m, 0m, t0.AddMinutes(2), "Issue");
        await Db.ChangeStatusAsync("TBStockDoc", issue, "Posted");

        var movements = await Db.QueryMovementsAsync("TBFifo", "[DocumentMetaId] = '" + issue + "'");
        Assert.AreEqual(1, movements.Count, "расход даёт одно движение по TBFifo");
        Assert.AreEqual(-15m, Convert.ToDecimal(movements[0]["Quantity"]), "количество расхода");
        Assert.AreEqual(-2000m, Convert.ToDecimal(movements[0]["Amount"]),
            "движок заменяет Amount на вычисленную себестоимость: 10*100 + 5*200 = 2000");

        var balances = await Db.QueryBalancesAsync("TBFifo", "[Item] = '" + item + "'");
        Assert.AreEqual(1, balances.Count, "одна строка остатка по номенклатуре");
        Assert.AreEqual(5m, Convert.ToDecimal(balances[0]["Quantity"]), "остаток количества: 20 - 15");
        Assert.AreEqual(1000m, Convert.ToDecimal(balances[0]["Amount"]), "остаточная стоимость слоёв: 3000 - 2000");
        Log("FIFO подтверждён: COGS 2000, остаток 5 шт / 1000.");
    }

    private Task<Guid> CreateDocAsync(Guid warehouse, Guid item, decimal qty, decimal amount, DateTime date, string subtype)
        => Db.CreateDocumentAsync(
            "TBStockDoc",
            new Dictionary<string, object?> { ["Warehouse"] = warehouse, ["DocumentDate"] = date },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Items"] = new IDictionary<string, object?>[]
                {
                    new Dictionary<string, object?> { ["Item"] = item, ["Quantity"] = qty, ["Amount"] = amount },
                },
            },
            subtype);
}