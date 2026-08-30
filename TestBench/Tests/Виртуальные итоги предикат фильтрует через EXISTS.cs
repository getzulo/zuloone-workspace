using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Предикат (MIQS VTOTAL_PREDICATES) — членство через EXISTS: склад, чьё имя
// подпадает под SQL предиката, попадает и в предикатную группу NamedOnly, и в
// обычную StockMoves (2 группы); чужой склад — только в StockMoves (1 группа).
public class VirtualTotalPredicateTest : IntegrationTestScriptBase
{
    [IntegrationTest("Virtual totals: predicate filters rows via EXISTS")]
    public async Task PredicateFilters()
    {
        var namedWh = await Db.InsertAsync("TBWarehouse",
            new Dictionary<string, object?> { ["Name"] = "VTP-" + Guid.NewGuid().ToString("N").Substring(0, 6) });
        var otherWh = Db.NewId();
        var item = Db.NewId();
        await Db.PostMovementAsync("TBStock", new DateTime(2026, 3, 2),
            new Dictionary<string, object?> { ["Warehouse"] = namedWh, ["Item"] = item },
            new Dictionary<string, decimal> { ["Quantity"] = 5m });
        await Db.PostMovementAsync("TBStock", new DateTime(2026, 3, 2),
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