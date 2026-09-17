using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// «Ядерные тесты.Итоги»: движения по wh1 и wh2 хранятся в отдельных строках
// остатков и не мешают друг другу — ключ измерения (Warehouse, Item) уникален.
public class TbTwoKeysTest : IntegrationTestScriptBase
{
    [IntegrationTest("два ключа измерения независимы")]
    public async Task TwoKeysAccumulateIndependently()
    {
        var wh1 = Db.NewId();
        var wh2 = Db.NewId();
        var item = Db.NewId();

        await Db.PostMovementAsync("TBStock", new DateTime(2026, 1, 1),
            new Dictionary<string, object?> { ["Warehouse"] = wh1, ["Item"] = item },
            new Dictionary<string, decimal> { ["Quantity"] = 10m });
        await Db.PostMovementAsync("TBStock", new DateTime(2026, 1, 2),
            new Dictionary<string, object?> { ["Warehouse"] = wh1, ["Item"] = item },
            new Dictionary<string, decimal> { ["Quantity"] = -3m });
        await Db.PostMovementAsync("TBStock", new DateTime(2026, 1, 1),
            new Dictionary<string, object?> { ["Warehouse"] = wh2, ["Item"] = item },
            new Dictionary<string, decimal> { ["Quantity"] = 7m });

        var b1 = await Db.QueryBalancesAsync("TBStock",
            "[Warehouse] = '" + wh1 + "' AND [Item] = '" + item + "'");
        var b2 = await Db.QueryBalancesAsync("TBStock",
            "[Warehouse] = '" + wh2 + "' AND [Item] = '" + item + "'");

        Assert.AreEqual(1, b1.Count, "одна строка остатка для wh1/item");
        Assert.AreEqual(7m, Convert.ToDecimal(b1[0]["Quantity"]), "wh1: 10 − 3 = 7");
        Assert.AreEqual(1, b2.Count, "одна строка остатка для wh2/item");
        Assert.AreEqual(7m, Convert.ToDecimal(b2[0]["Quantity"]), "wh2 = 7 нетронут движениями wh1");
        Log("wh1 остаток = 7, wh2 остаток = 7 — ключи независимы.");
    }
}