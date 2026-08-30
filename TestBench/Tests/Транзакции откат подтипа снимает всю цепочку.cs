using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Обязательно: тестовым скриптам этот namespace НЕ выдаётся глобальным using.
using ZuloOne.Runtime.Generated;

// «Ядерные тесты.Транзакции»: снятие с проведения (статус Reverted) снимает движения
// ВСЕЙ цепочки подтипа — обеих её частей.
//
// Документ строится, проводится и распроводится типизированным IDocumentManager,
// остатки и движения читаются ITotalsManager: и подтип, и статус SaveDocumentAsync
// проводит через движок, поэтому обращаться к базе тесту незачем.
public class TransactionChainRevertTest : IntegrationTestScriptBase
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("Транзакции: откат подтипа снимает всю цепочку")]
    public async Task RevertRollsBackWholeChain()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();

        var doc = await CreateDocAsync(warehouse, item, 5m, 500m, DateTime.UtcNow, TBStockDoc.Subtypes.ReceiptChain);

        // Черновик движений не несёт. Проверяем ДО перехода — иначе утверждения
        // ниже проходят и в случае, когда документ провёлся сам при сохранении.
        Assert.AreEqual(0m, await StockQtyAsync(warehouse, item), "черновик не порождает движений TBStock");

        doc.StatusValue = "Posted";
        await DocumentManager.SaveDocumentAsync(doc);
        Assert.AreEqual(105m, await StockQtyAsync(warehouse, item), "после проведения остаток 105 (5 + 100)");

        doc.StatusValue = "Reverted";
        await DocumentManager.SaveDocumentAsync(doc);
        Assert.AreEqual(0m, await StockQtyAsync(warehouse, item), "после отката остаток 0 — сняты обе части цепочки");

        var movements = await TotalsManager.QueryMovementsAsync("TBStock", "[DocumentMetaId] = '" + doc.MetaId + "'");
        Assert.AreEqual(0, movements.Count, "движений цепочки не осталось");

        var stored = (await DocumentManager.GetDocumentAsync<TBStockDoc>(doc.MetaId))!;
        Assert.AreEqual("Reverted", stored.StatusValue, "статус документа — Reverted");

        Log("Откат цепочки подтверждён: остаток 0, движений нет.");
    }

    private static async Task<decimal> StockQtyAsync(Guid warehouse, Guid item)
    {
        var rows = await TotalsManager.QueryBalancesAsync(
            "TBStock", "[Warehouse] = '" + warehouse + "' AND [Item] = '" + item + "'");
        return rows.Count == 0 ? 0m : Convert.ToDecimal(rows[0]["Quantity"]);
    }

    private static async Task<TBStockDoc> CreateDocAsync(
        Guid warehouse, Guid item, decimal qty, decimal amount, DateTime date, string subtype)
    {
        var doc = await DocumentManager.NewDocumentAsync<TBStockDoc>(subtype);
        doc.Warehouse = warehouse;
        doc.DocumentDate = date;
        doc.Items.Add(new TBLinesTablePartRow { Item = item, Quantity = qty, Amount = amount });
        await DocumentManager.SaveDocumentAsync(doc);
        return doc;
    }
}
