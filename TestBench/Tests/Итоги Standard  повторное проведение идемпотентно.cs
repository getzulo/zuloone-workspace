using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// «Ядерные тесты. Итоги»: провести → снять → провести снова не задваивает остаток.
public class TotalsRepostIdempotenceTest : IntegrationTestScriptBase
{
    [IntegrationTest("Standard: повторное проведение идемпотентно")]
    public async Task RepostIsIdempotent()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();

        var docId = await CreateDocAsync(warehouse, item, 7m, 700m, DateTime.UtcNow, "Receipt");

        await Db.ChangeStatusAsync("TBStockDoc", docId, "Posted");
        Assert.AreEqual(7m, await StockQtyAsync(warehouse, item), "первое проведение: остаток 7");

        await Db.ChangeStatusAsync("TBStockDoc", docId, "Reverted");
        Assert.AreEqual(0m, await StockQtyAsync(warehouse, item), "после снятия: остаток 0");

        await Db.ChangeStatusAsync("TBStockDoc", docId, "Posted");
        Assert.AreEqual(7m, await StockQtyAsync(warehouse, item), "повторное проведение: ровно 7, без задвоения");

        var movements = await Db.QueryMovementsAsync("TBStock", "[DocumentMetaId] = '" + docId + "'");
        Assert.AreEqual(1, movements.Count, "после перепроведения — ровно одно движение");
        Log("Идемпотентность подтверждена: остаток 7, движение одно.");
    }

    private async Task<decimal> StockQtyAsync(Guid warehouse, Guid item)
    {
        var rows = await Db.QueryBalancesAsync("TBStock", "[Warehouse] = '" + warehouse + "' AND [Item] = '" + item + "'");
        return rows.Count == 0 ? 0m : Convert.ToDecimal(rows[0]["Quantity"]);
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