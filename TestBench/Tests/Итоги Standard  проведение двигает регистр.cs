using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// «Ядерные тесты. Итоги»: проведение Receipt по TBStockDoc двигает регистр TBStock.
public class TotalsStandardPostingTest : IntegrationTestScriptBase
{
    [IntegrationTest("Standard: проведение двигает регистр")]
    public async Task PostingMovesRegister()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();

        var docId = await CreateDocAsync(warehouse, item, 7m, 700m, DateTime.UtcNow, "Receipt");
        await Db.ChangeStatusAsync("TBStockDoc", docId, "Posted");

        var balances = await Db.QueryBalancesAsync("TBStock", "[Warehouse] = '" + warehouse + "' AND [Item] = '" + item + "'");
        Assert.AreEqual(1, balances.Count, "одна строка остатка по паре склад/номенклатура");
        Assert.AreEqual(7m, Convert.ToDecimal(balances[0]["Quantity"]), "остаток Quantity после проведения");

        var movements = await Db.QueryMovementsAsync("TBStock", "[DocumentMetaId] = '" + docId + "'");
        Assert.AreEqual(1, movements.Count, "Receipt даёт ровно одно движение по TBStock");
        foreach (var movement in movements)
        {
            Assert.IsNotNull(movement["ScriptMetaId"], "каждое движение несёт происхождение (ScriptMetaId)");
        }
        Log("Проведение подтверждено: остаток 7, движений " + movements.Count + ".");
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