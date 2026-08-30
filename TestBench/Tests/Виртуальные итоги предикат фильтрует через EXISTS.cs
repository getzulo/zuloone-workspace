using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы справочников (TBWarehouse) — тестовым скриптам этот namespace
// НЕ приходит глобальным using'ом.
using ZuloOne.Runtime.Generated;

// Предикат (MIQS VTOTAL_PREDICATES) — членство через EXISTS: склад, чьё имя
// подпадает под SQL предиката, попадает и в предикатную группу NamedOnly, и в
// обычную StockMoves (2 группы); чужой склад — только в StockMoves (1 группа).
//
// Склад создаётся типизированной записью через IDictionaryManager, движения идут
// через ITotalsManager. Db остаётся только на QueryVirtualTotalAsync: у виртуальных
// итогов менеджера нет, а движок виртуальных итогов здесь и есть предмет проверки.
public class VirtualTotalPredicateTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("Virtual totals: predicate filters rows via EXISTS")]
    public async Task PredicateFilters()
    {
        var warehouse = DictionaryManager.NewRecord<TBWarehouse>();
        warehouse.Name = "VTP-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        warehouse = await DictionaryManager.SaveRecordAsync(warehouse);
        var namedWh = warehouse.MetaId;

        var otherWh = Db.NewId();
        var item = Db.NewId();

        // Движения вне документа: владельца называем явно (null) — ITotalsManager
        // не умалчивает его, как это делал фасад.
        await TotalsManager.PostMovementAsync("TBStock", null, new DateTime(2026, 3, 2),
            new Dictionary<string, object?> { ["Warehouse"] = namedWh, ["Item"] = item },
            new Dictionary<string, decimal> { ["Quantity"] = 5m });
        await TotalsManager.PostMovementAsync("TBStock", null, new DateTime(2026, 3, 2),
            new Dictionary<string, object?> { ["Warehouse"] = otherWh, ["Item"] = item },
            new Dictionary<string, decimal> { ["Quantity"] = 3m });

        var rows = await Db.QueryVirtualTotalAsync("TBCombinedStock");
        var namedGroups = rows.Where(r => namedWh.Equals(r["Warehouse"]))
            .Select(r => r["GroupMetaId"]).Distinct().Count();
        var otherGroups = rows.Where(r => otherWh.Equals(r["Warehouse"]))
            .Select(r => r["GroupMetaId"]).Distinct().Count();
        Assert.IsTrue(namedGroups == 2, "именованный склад в 2 группах (Stock+NamedOnly): {0}", namedGroups);
        Assert.IsTrue(otherGroups == 1, "прочий склад только в StockMoves: {0}", otherGroups);
    }
}
