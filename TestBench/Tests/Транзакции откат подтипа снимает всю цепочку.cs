using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// «Ядерные тесты.Транзакции»: снятие с проведения (статус Reverted) снимает движения
// ВСЕЙ цепочки подтипа — обеих её частей.
public class TransactionChainRevertTest : IntegrationTestScriptBase
{
    [IntegrationTest("Транзакции: откат подтипа снимает всю цепочку")]
    public async Task RevertRollsBackWholeChain()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();

        var docId = await CreateDocAsync(warehouse, item, 5m, 500m, DateTime.UtcNow, "ReceiptChain");
        await Db.ChangeStatusAsync("TBStockDoc", docId, "Posted");
        Assert.AreEqual(105m, await StockQtyAsync(warehouse, item), "после проведения остаток 105 (5 + 100)");

        await Db.ChangeStatusAsync("TBStockDoc", docId, "Reverted");
        Assert.AreEqual(0m, await StockQtyAsync(warehouse, item), "после отката остаток 0 — сняты обе части цепочки");

        var movements = await Db.QueryMovementsAsync("TBStock", "[DocumentMetaId] = '" + docId + "'");
        Assert.AreEqual(0, movements.Count, "движений цепочки не осталось");
        Log("Откат цепочки подтверждён: остаток 0, движений нет.");
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