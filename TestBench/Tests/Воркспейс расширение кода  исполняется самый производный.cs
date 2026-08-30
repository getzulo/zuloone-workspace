using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы (TBStockDoc, TBLinesTablePartRow). Тест-скрипты НЕ получают
// это пространство имён глобальным using — без него они просто не находятся.
using ZuloOne.Runtime.Generated;

// Проведение Receipt со строкой-маркером (Amount=777.77): вместо TBReceiptTx
// рантайм инстанцирует TBReceiptTx_TestBenchExt — base.GetTransactions даёт
// базовое движение (+qty), расширение добавляет второе (+qty) → остаток 2×qty.
// Обычная строка (Amount=700) остаётся 1×qty — расширение хирургично.
//
// Документ собирается типизированно через IDocumentManager, регистр читается
// через ITotalsManager: подмена скрипта проверяется на том же пути, которым
// документ создаёт прикладной код.
public class CodeExtensionTest : IntegrationTestScriptBase
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("Workspace: code extension - most-derived script runs")]
    public async Task MostDerivedScriptRuns()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();
        var slice = "[Warehouse] = '" + warehouse + "' AND [Item] = '" + item + "'";
        var marked = await CreateDocAsync(warehouse, item, 5m, 777.77m, DateTime.UtcNow, TBStockDoc.Subtypes.Receipt);

        // Состояние ДО перехода: пока документ не проведён, регистр пуст — иначе
        // «10» ниже могло бы означать что угодно, включая разноску при сохранении.
        Assert.AreEqual(0, (await TotalsManager.QueryBalancesAsync("TBStock", slice)).Count,
            "черновик не должен порождать остатков TBStock");

        // У TBStockDoc движения вешает СТАТУС Posted, а не подтип. Проведение —
        // это присвоение плюс сохранение: SaveDocumentAsync проводит изменившийся
        // StatusValue через движок, симметрично смене подтипа.
        marked.StatusValue = "Posted";
        await DocumentManager.SaveDocumentAsync(marked);

        var balances = await TotalsManager.QueryBalancesAsync("TBStock", slice);
        Assert.AreEqual(10m, balances.Sum(b => Convert.ToDecimal(b["Quantity"])),
            "маркерная строка: база +5 и расширение +5 (самый производный класс исполнился)");

        var warehouse2 = Db.NewId();
        var item2 = Db.NewId();
        var slice2 = "[Warehouse] = '" + warehouse2 + "' AND [Item] = '" + item2 + "'";
        var plain = await CreateDocAsync(warehouse2, item2, 5m, 700m, DateTime.UtcNow, TBStockDoc.Subtypes.Receipt);
        plain.StatusValue = "Posted";
        await DocumentManager.SaveDocumentAsync(plain);

        var balances2 = await TotalsManager.QueryBalancesAsync("TBStock", slice2);
        Assert.AreEqual(5m, balances2.Sum(b => Convert.ToDecimal(b["Quantity"])),
            "обычная строка: только базовое движение — расширение хирургично");
    }

    private async Task<TBStockDoc> CreateDocAsync(Guid warehouse, Guid item, decimal qty, decimal amount, DateTime date, string subtype)
    {
        var doc = await DocumentManager.NewDocumentAsync<TBStockDoc>(subtype);
        doc.Warehouse = warehouse;
        doc.DocumentDate = date;
        doc.Items.Add(new TBLinesTablePartRow { Item = item, Quantity = qty, Amount = amount });
        await DocumentManager.SaveDocumentAsync(doc);
        return doc;
    }
}
