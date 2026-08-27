using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Проведение Receipt со строкой-маркером (Amount=777.77): вместо TBReceiptTx
// рантайм инстанцирует TBReceiptTx_TestBenchExt — base.GetTransactions даёт
// базовое движение (+qty), расширение добавляет второе (+qty) → остаток 2×qty.
// Обычная строка (Amount=700) остаётся 1×qty — расширение хирургично.
public class CodeExtensionTest : IntegrationTestScriptBase
{
    [IntegrationTest("Workspace: code extension - most-derived script runs")]
    public async Task MostDerivedScriptRuns()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();
        var marked = await CreateDocAsync(warehouse, item, 5m, 777.77m, DateTime.UtcNow, "Receipt");
        await Db.ChangeStatusAsync("TBStockDoc", marked, "Posted");
        var balances = await Db.QueryBalancesAsync("TBStock", "[Warehouse] = '" + warehouse + "' AND [Item] = '" + item + "'");
        Assert.AreEqual(10m, balances.Sum(b => Convert.ToDecimal(b["Quantity"])),
            "маркерная строка: база +5 и расширение +5 (самый производный класс исполнился)");

        var warehouse2 = Db.NewId();
        var item2 = Db.NewId();
        var plain = await CreateDocAsync(warehouse2, item2, 5m, 700m, DateTime.UtcNow, "Receipt");
        await Db.ChangeStatusAsync("TBStockDoc", plain, "Posted");
        var balances2 = await Db.QueryBalancesAsync("TBStock", "[Warehouse] = '" + warehouse2 + "' AND [Item] = '" + item2 + "'");
        Assert.AreEqual(5m, balances2.Sum(b => Convert.ToDecimal(b["Quantity"])),
            "обычная строка: только базовое движение — расширение хирургично");
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