using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Виртуальный итог считает поверх TR_ без собственных таблиц: движение по
// TBStock видно через TBCombinedStock со своей группой и переменной Qty.
public class VirtualTotalDslTest : IntegrationTestScriptBase
{
    [IntegrationTest("Virtual totals: DSL computes over TR_")]
    public async Task DslComputesRows()
    {
        var wh = Db.NewId();
        var item = Db.NewId();
        await Db.PostMovementAsync("TBStock", new DateTime(2026, 3, 1),
            new Dictionary<string, object?> { ["Warehouse"] = wh, ["Item"] = item },
            new Dictionary<string, decimal> { ["Quantity"] = 7m });

        var compile = await Db.CompileVirtualTotalAsync("TBCombinedStock");
        Assert.IsTrue(compile.Success, "компиляция DSL: {0}", string.Join("; ", compile.Errors));
        Assert.IsTrue(compile.GroupCount == 4, "групп из DSL: {0}", compile.GroupCount);
        Assert.IsTrue(compile.RootGroupCount == 2, "корневых групп (NamedOnly+AllMoves): {0}", compile.RootGroupCount);

        var rows = await Db.QueryVirtualTotalAsync("TBCombinedStock");
        var mine = rows.Where(r => wh.Equals(r["Warehouse"])).ToList();
        Assert.IsTrue(mine.Count >= 1, "движение видно через виртуальный итог");
        Assert.IsTrue(mine.Any(r => Convert.ToDecimal(r["Qty"]) == 7m), "Qty проброшен из ресурса Quantity");
        Assert.IsTrue(mine.All(r => r["GroupMetaId"] != null), "каждая строка несёт группу");
    }
}