using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы (TBStockDoc, TBLinesTablePartRow) — тестовым скриптам
// это пространство имён глобальным using НЕ выдаётся.
using ZuloOne.Runtime.Generated;

// «Ядерные тесты. Итоги»: снятие с проведения (статус Reverted) откатывает движения TBStock.
// Остатки и движения читаются через ITotalsManager — ту же дверь, что и бизнес-код.
public class TotalsStandardRevertTest : IntegrationTestScriptBase
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("Standard: снятие с проведения откатывает движения")]
    public async Task RevertRollsBackMovements()
    {
        var warehouse = Db.NewId(); // Db.NewId() — законный остаток: генерация id, не доступ к данным.
        var item = Db.NewId();

        var doc = await NewDocAsync(warehouse, item, 7m, 700m, DateTime.UtcNow, TBStockDoc.Subtypes.Receipt);
        // Черновик регистр не двигает — иначе «после проведения 7» ничего не доказывает.
        Assert.AreEqual(0m, await StockQtyAsync(warehouse, item), "до проведения остаток 0");

        // И проведение, и снятие с него — ПРИСВОЕНИЕ статуса плюс сохранение:
        // SaveDocumentAsync проводит изменившийся StatusValue через движок
        // (SetStatusAsync), симметрично смене подтипа.
        doc.StatusValue = "Posted";
        await DocumentManager.SaveDocumentAsync(doc);
        Assert.AreEqual(7m, await StockQtyAsync(warehouse, item), "после проведения остаток 7");

        doc.StatusValue = "Reverted";
        await DocumentManager.SaveDocumentAsync(doc);
        Assert.AreEqual(0m, await StockQtyAsync(warehouse, item), "после снятия с проведения остаток 0");

        var movements = await TotalsManager.QueryMovementsAsync("TBStock", "[DocumentMetaId] = '" + doc.MetaId + "'");
        Assert.AreEqual(0, movements.Count, "движения документа удалены");

        // Откат обязан быть записан и в заголовке, а не только в регистре.
        var stored = (await DocumentManager.GetDocumentAsync<TBStockDoc>(doc.MetaId))!;
        Assert.AreEqual("Reverted", stored.StatusValue, "статус документа — Reverted");

        Log("Откат подтверждён: остаток 0, движений нет.");
    }

    // Срез регистра адресуется измерениями, а не SQL-строкой: у менеджера итогов
    // для этого есть именно такая дверь, и «нет строки = ноль» он берёт на себя.
    private Task<decimal> StockQtyAsync(Guid warehouse, Guid item)
        => TotalsManager.GetBalanceAsync("TBStock", "Quantity",
            new Dictionary<string, object?> { ["Warehouse"] = warehouse, ["Item"] = item });

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
