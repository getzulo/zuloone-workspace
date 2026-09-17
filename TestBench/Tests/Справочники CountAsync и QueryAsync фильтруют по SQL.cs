using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// «Ядерные тесты.Справочники»: CountAsync и QueryAsync с SQL WHERE фильтруют
// по содержимому — два целевых + один сторонний, LIKE отдаёт ровно 2.
public class TbQueryCountTest : IntegrationTestScriptBase
{
    [IntegrationTest("CountAsync и QueryAsync фильтруют по SQL")]
    public async Task QueryAndCountWithFilter()
    {
        var prefix = "QCTEST" + Db.NewId().ToString("N").Substring(0, 8).ToUpperInvariant();
        await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = prefix + "-A" });
        await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = prefix + "-B" });
        await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = "OTHER-" + Db.NewId().ToString("N").Substring(0, 8).ToUpperInvariant() });

        var count = await Db.CountAsync("TBWarehouse", $"[Name] LIKE '{prefix}%'");
        Assert.AreEqual(2, count, "CountAsync с фильтром: ожидается 2, получено {0}", count);

        var rows = await Db.QueryAsync("TBWarehouse", $"[Name] LIKE '{prefix}%'");
        Assert.AreEqual(2, rows.Count, "QueryAsync с фильтром: ожидается 2 строки, получено {0}", rows.Count);
        Assert.IsTrue(rows.All(r => r["Name"]?.ToString()?.StartsWith(prefix) == true),
            "QueryAsync возвращает только строки, подходящие под фильтр");
        Log("Count=" + count + ", rows=" + rows.Count + " — оба метода фильтруют корректно.");
    }
}