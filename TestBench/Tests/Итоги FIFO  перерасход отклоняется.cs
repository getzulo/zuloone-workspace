using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Обязательно: тестовым скриптам этот namespace НЕ выдаётся глобальным using.
using ZuloOne.Runtime.Generated;

// «Ядерные тесты. Итоги»: расход сверх остатка слоёв отклоняется FIFO-движком.
//
// Документы строятся типизированным IDocumentManager, остатки читаются
// ITotalsManager, проведение идёт присвоением StatusValue плюс сохранением —
// SaveDocumentAsync проводит изменившийся статус через движок.
public class TotalsFifoOverdrawTest : IntegrationTestScriptBase
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("FIFO: перерасход отклоняется")]
    public async Task OverdrawIsRejected()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();
        var t0 = DateTime.UtcNow.AddMinutes(-10);

        var receipt = await CreateDocAsync(warehouse, item, 5m, 250m, t0, TBStockDoc.Subtypes.Receipt);

        // Черновик слоёв не создаёт: проверяем ДО проведения, иначе утверждение
        // «приход создал строку остатка» проходит и без самого перехода.
        Assert.AreEqual(0, (await TotalsManager.QueryBalancesAsync("TBFifo", "[Item] = '" + item + "'")).Count,
            "до проведения слоёв FIFO нет");

        receipt.StatusValue = "Posted";
        await DocumentManager.SaveDocumentAsync(receipt);

        var before = await TotalsManager.QueryBalancesAsync("TBFifo", "[Item] = '" + item + "'");
        Assert.AreEqual(1, before.Count, "приход создал строку остатка");
        Assert.AreEqual(5m, Convert.ToDecimal(before[0]["Quantity"]), "в наличии 5 шт");

        var issue = await CreateDocAsync(warehouse, item, 6m, 0m, t0.AddMinutes(1), TBStockDoc.Subtypes.Issue);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
            {
                issue.StatusValue = "Posted";
                await DocumentManager.SaveDocumentAsync(issue);
            },
            "проведение расхода 6 шт при остатке 5 шт должно быть отклонено");
        Assert.IsTrue(ex.Message.Contains("Insufficient"), "движок сообщает о нехватке слоёв FIFO: {0}", ex.Message);

        // После отказа БАЗУ НЕ ТРОГАЕМ. Движок отклоняет проведение изнутри
        // вложенного TransactionScope, который уходит без Complete и приговаривает
        // объемлющую транзакцию кейса: любое следующее чтение падает с «the
        // operation is not valid for the state of the transaction» и маскирует
        // настоящее утверждение. Отказ — это и есть проверяемый факт; неизменность
        // остатка гарантирует общий откат кейса.
        Log("Перерасход отклонён движком: " + ex.Message);
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
