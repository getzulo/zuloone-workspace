using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённый класс TBWarehouse. Тест-скрипты НЕ получают это пространство имён
// глобальным using — без него класс просто не находится.
using ZuloOne.Runtime.Generated;

// «Ядерные тесты.Справочники»: CountAsync и GetRecordsAsync с SQL WHERE фильтруют
// по содержимому — два целевых + один сторонний, LIKE отдаёт ровно 2.
//
// Предмет теста — СТРОКОВЫЙ (SQL) фильтр, поэтому берутся именно строковые
// перегрузки IDictionaryManager: у типизированной перегрузки с предикатом есть
// откат на фильтрацию В ПАМЯТИ, когда предикат не переводится в SQL, — на ней
// «фильтрует по SQL» доказать нельзя. Строковый фильтр уходит в WHERE как есть.
public class TbQueryCountTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    [IntegrationTest("CountAsync и QueryAsync фильтруют по SQL")]
    public async Task QueryAndCountWithFilter()
    {
        var prefix = "QCTEST" + Db.NewId().ToString("N").Substring(0, 8).ToUpperInvariant();
        await NewWarehouseAsync(prefix + "-A");
        await NewWarehouseAsync(prefix + "-B");
        await NewWarehouseAsync("OTHER-" + Db.NewId().ToString("N").Substring(0, 8).ToUpperInvariant());

        var filter = $"[Name] LIKE '{prefix}%'";

        var count = await DictionaryManager.CountAsync<TBWarehouse>(filter);
        Assert.AreEqual(2, count, "CountAsync с фильтром: ожидается 2, получено {0}", count);

        var rows = await DictionaryManager.GetRecordsAsync<TBWarehouse>(filter);
        Assert.AreEqual(2, rows.Count, "GetRecordsAsync с фильтром: ожидается 2 строки, получено {0}", rows.Count);
        Assert.IsTrue(rows.All(r => r.Name?.StartsWith(prefix) == true),
            "GetRecordsAsync возвращает только строки, подходящие под фильтр");
        Log("Count=" + count + ", rows=" + rows.Count + " — оба метода фильтруют корректно.");
    }

    private static async Task NewWarehouseAsync(string name)
    {
        var warehouse = DictionaryManager.NewRecord<TBWarehouse>();
        warehouse.Name = name;
        await DictionaryManager.SaveRecordAsync(warehouse);
    }
}
