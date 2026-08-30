using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// «Ядерные тесты. Итоги»: снятие с проведения (статус Reverted) откатывает движения TBStock.
public class TotalsStandardRevertTest : IntegrationTestScriptBase
{
    [IntegrationTest("Standard: снятие с проведения откатывает движения")]
    public async Task RevertRollsBackMovements()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();

        var docId = await CreateDocAsync(warehouse, item, 7m, 700m, DateTime.UtcNow, "Receipt");
        await Db.ChangeStatusAsync("TBStockDoc", docId, "Posted");
        Assert.AreEqual(7m, await StockQtyAsync(warehouse, item), "после проведения остаток 7");

        await Db.ChangeStatusAsync("TBStockDoc", docId, "Reverted");
        Assert.AreEqual(0m, await StockQtyAsync(warehouse, item), "после снятия с проведения остаток 0");

        var movements = await Db.QueryMovementsAsync("TBStock", "[DocumentMetaId] = '" + docId + "'");
        Assert.AreEqual(0, movements.Count, "движения документа удалены");
        Log("Откат подтверждён: остаток 0, движений нет.");
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