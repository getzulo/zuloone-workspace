using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы (TBStockDoc, TBLinesTablePartRow) — тестовым скриптам этот
// namespace НЕ приходит глобальным using'ом.
using ZuloOne.Runtime.Generated;

// «Ядерные тесты. Итоги»: FIFO-движок списывает себестоимость по слоям в порядке дат.
// Слой 1: 10 шт по 100 (1000). Слой 2: 10 шт по 200 (2000). Расход 15 шт →
// COGS = 10*100 + 5*200 = 2000; остаток 5 шт стоимостью 1000.
//
// Документы строятся типизированно (NewDocumentAsync<TBStockDoc> → TBLinesTablePartRow
// → SaveDocumentAsync), регистр читается через ITotalsManager.
public class TotalsFifoCogsTest : IntegrationTestScriptBase
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("FIFO: себестоимость по слоям")]
    public async Task OutcomeCostIsComputedAcrossLayers()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();
        var t0 = DateTime.UtcNow.AddMinutes(-10);

        var receipt1 = await NewDocAsync(warehouse, item, 10m, 1000m, t0, TBStockDoc.Subtypes.Receipt);

        // Сохранённый черновик не двигает регистр: слои появляются только при
        // переводе в Posted. Без этой проверки цифры ниже сошлись бы и в случае,
        // когда всё посчиталось на сохранении, а переход статуса не сделал ничего.
        Assert.AreEqual(0m, await TotalsManager.GetBalanceAsync("TBFifo", "Quantity",
            new Dictionary<string, object?> { ["Item"] = item }),
            "черновик прихода не создаёт слой FIFO");

        // TBStockDoc проводится СТАТУСОМ: присвоение плюс сохранение, а
        // SaveDocumentAsync проводит изменившийся StatusValue через движок —
        // ровно так же, как смену подтипа.
        await PostAsync(receipt1);

        var receipt2 = await NewDocAsync(warehouse, item, 10m, 2000m, t0.AddMinutes(1), TBStockDoc.Subtypes.Receipt);
        await PostAsync(receipt2);

        var issue = await NewDocAsync(warehouse, item, 15m, 0m, t0.AddMinutes(2), TBStockDoc.Subtypes.Issue);
        await PostAsync(issue);

        var movements = await TotalsManager.QueryMovementsAsync("TBFifo", "[DocumentMetaId] = '" + issue.MetaId + "'");
        Assert.AreEqual(1, movements.Count, "расход даёт одно движение по TBFifo");
        Assert.AreEqual(-15m, Convert.ToDecimal(movements[0]["Quantity"]), "количество расхода");
        Assert.AreEqual(-2000m, Convert.ToDecimal(movements[0]["Amount"]),
            "движок заменяет Amount на вычисленную себестоимость: 10*100 + 5*200 = 2000");

        var balances = await TotalsManager.QueryBalancesAsync("TBFifo", "[Item] = '" + item + "'");
        Assert.AreEqual(1, balances.Count, "одна строка остатка по номенклатуре");
        Assert.AreEqual(5m, Convert.ToDecimal(balances[0]["Quantity"]), "остаток количества: 20 - 15");
        Assert.AreEqual(1000m, Convert.ToDecimal(balances[0]["Amount"]), "остаточная стоимость слоёв: 3000 - 2000");
        Log("FIFO подтверждён: COGS 2000, остаток 5 шт / 1000.");
    }

    private static Task PostAsync(TBStockDoc doc)
    {
        doc.StatusValue = "Posted";
        return DocumentManager.SaveDocumentAsync(doc);
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
