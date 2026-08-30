using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы (TBStockDoc, TBLinesTablePartRow) — тестовым скриптам этот
// namespace НЕ приходит глобальным using'ом.
using ZuloOne.Runtime.Generated;

// «Ядерные тесты.Транзакции»: подтип ReceiptChain несёт ДВА транзакционных скрипта
// (order 0: +Quantity по строке; order 1: плоские +100). Проведение выполняет всю
// цепочку, и каждое движение несёт происхождение СВОЕГО скрипта.
//
// Документ строится и проводится типизированно: NewDocumentAsync<TBStockDoc> →
// строки как TBLinesTablePartRow → SaveDocumentAsync, как это делает бизнес-код.
public class TransactionChainWholeTest : IntegrationTestScriptBase
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("Транзакции: цепочка подтипа выполняется целиком")]
    public async Task SubtypeChainRunsWhole()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();

        var doc = await NewDocAsync(warehouse, item, 5m, 500m, DateTime.UtcNow, TBStockDoc.Subtypes.ReceiptChain);

        // Сохранённый черновик движений НЕ порождает: TBStockDoc проводится
        // СТАТУСОМ, и без этой проверки «остаток 105» ниже зеленел бы и в случае,
        // когда цепочку выполнило само сохранение, а переход не сделал ничего.
        Assert.AreEqual(0m, await TotalsManager.GetBalanceAsync("TBStock", "Quantity",
            new Dictionary<string, object?> { ["Warehouse"] = warehouse, ["Item"] = item }),
            "до перевода в Posted движений по паре склад/номенклатура нет");

        // Проведение — переход СТАТУСА Draft → Posted, и делается он присвоением
        // плюс сохранением: SaveDocumentAsync отдаёт изменившийся StatusValue
        // движку (SetStatusAsync) ровно так же, как изменившийся Subtype.
        doc.StatusValue = "Posted";
        await DocumentManager.SaveDocumentAsync(doc);

        var balances = await TotalsManager.QueryBalancesAsync("TBStock",
            "[Warehouse] = '" + warehouse + "' AND [Item] = '" + item + "'");
        Assert.AreEqual(1, balances.Count, "одна строка остатка по паре склад/номенклатура");
        Assert.AreEqual(105m, Convert.ToDecimal(balances[0]["Quantity"]), "5 из строки + плоские 100 — вся цепочка выполнилась");

        var movements = await TotalsManager.QueryMovementsAsync("TBStock", "[DocumentMetaId] = '" + doc.MetaId + "'");
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

    private async Task<TBStockDoc> NewDocAsync(Guid warehouse, Guid item, decimal qty, decimal amount, DateTime date, string subtype)
    {
        var doc = await DocumentManager.NewDocumentAsync<TBStockDoc>(subtype);
        doc.Warehouse = warehouse;
        doc.DocumentDate = date;
        doc.Items.Add(new TBLinesTablePartRow { Item = item, Quantity = qty, Amount = amount });
        await DocumentManager.SaveDocumentAsync(doc);
        return doc;
    }
}
