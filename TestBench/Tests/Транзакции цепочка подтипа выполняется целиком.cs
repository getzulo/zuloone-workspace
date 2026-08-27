using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// «Ядерные тесты.Транзакции»: подтип ReceiptChain несёт ДВА транзакционных скрипта
// (order 0: +Quantity по строке; order 1: плоские +100). Проведение выполняет всю
// цепочку, и каждое движение несёт происхождение СВОЕГО скрипта.
public class TransactionChainWholeTest : IntegrationTestScriptBase
{
    [IntegrationTest("Транзакции: цепочка подтипа выполняется целиком")]
    public async Task SubtypeChainRunsWhole()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();

        var docId = await CreateDocAsync(warehouse, item, 5m, 500m, DateTime.UtcNow, "ReceiptChain");
        await Db.ChangeStatusAsync("TBStockDoc", docId, "Posted");

        var balances = await Db.QueryBalancesAsync("TBStock", "[Warehouse] = '" + warehouse + "' AND [Item] = '" + item + "'");
        Assert.AreEqual(1, balances.Count, "одна строка остатка по паре склад/номенклатура");
        Assert.AreEqual(105m, Convert.ToDecimal(balances[0]["Quantity"]), "5 из строки + плоские 100 — вся цепочка выполнилась");

        var movements = await Db.QueryMovementsAsync("TBStock", "[DocumentMetaId] = '" + docId + "'");
        Assert.AreEqual(2, movements.Count, "по одному движению от каждого скрипта цепочки");
        var scriptIds = new HashSet<string>();
        foreach (var movement in movements)
        {
            Assert.IsNotNull(movement["ScriptMetaId"], "каждое движение несёт происхождение (ScriptMetaId)");
            scriptIds.Add(movement["ScriptMetaId"]!.ToString()!);
        }
        Assert.AreEqual(2, scriptIds.Count, "движения несут ДВА разных ScriptMetaId — записали оба скрипта цепочки");
        Log("Цепочка подтверждена: остаток 105, разных происхождений " + scriptIds.Count + ".");
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