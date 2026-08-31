using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Генерённые классы (TBStockDoc, TBLinesTablePartRow). Тест-скрипты НЕ получают
// это пространство имён глобальным using — без него они просто не находятся.
using ZuloOne.Runtime.Generated;

// «Ядерные тесты. Итоги»: проведение Receipt по TBStockDoc двигает регистр TBStock.
//
// Документ собирается и проводится типизированно через IDocumentManager
// (NewDocumentAsync → строки → SaveDocumentAsync → StatusValue → SaveDocumentAsync),
// регистр читается через ITotalsManager — те же двери, что и у прикладного кода.
public class TotalsStandardPostingTest : IntegrationTestScriptBase
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("Standard: проведение двигает регистр")]
    public async Task PostingMovesRegister()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();
        var slice = "[Warehouse] = '" + warehouse + "' AND [Item] = '" + item + "'";

        var doc = await CreateDocAsync(warehouse, item, 7m, 700m, DateTime.UtcNow, TBStockDoc.Subtypes.Receipt);

        // Состояние ДО перехода: неразнесённый документ регистр не двигает. Без
        // этой проверки утверждения ниже проходят и тогда, когда документ разнёс
        // себя сам при сохранении, — и о переходе Draft → Posted тест не говорит
        // ничего.
        Assert.AreEqual(0, (await TotalsManager.QueryBalancesAsync("TBStock", slice)).Count,
            "черновик не должен порождать остатков TBStock");

        // Проведение здесь — смена СТАТУСА (у TBStockDoc движения вешает статус
        // Posted, а не подтип), и делается она так же, как смена подтипа:
        // ПРИСВОЕНИЕ плюс сохранение. Изменившийся StatusValue SaveDocumentAsync
        // проводит через движок (IDocumentPostingService.SetStatusAsync), а не
        // пишет колонкой.
        doc.StatusValue = "Posted";
        await DocumentManager.SaveDocumentAsync(doc);

        var balances = await TotalsManager.QueryBalancesAsync("TBStock", slice);
        Assert.AreEqual(1, balances.Count, "одна строка остатка по паре склад/номенклатура");
        Assert.AreEqual(7m, Convert.ToDecimal(balances[0]["Quantity"]), "остаток Quantity после проведения");

        var movements = await TotalsManager.QueryMovementsAsync("TBStock", "[DocumentMetaId] = '" + doc.MetaId + "'");
        Assert.AreEqual(1, movements.Count, "Receipt даёт ровно одно движение по TBStock");
        foreach (var movement in movements)
        {
            Assert.IsNotNull(movement["ScriptMetaId"], "каждое движение несёт происхождение (ScriptMetaId)");
        }

        // Смена статуса — это полноценное сохранение заголовка, и оно обязано
        // оставить в покое поля, которые платформа выдала при вставке: статус
        // должен быть записан, а номер документа — пережить переход.
        var stored = (await DocumentManager.GetDocumentAsync<TBStockDoc>(doc.MetaId))!;
        Assert.AreEqual("Posted", stored.StatusValue, "новый статус записан в заголовок");
        Assert.IsFalse(string.IsNullOrWhiteSpace(stored.Number), "номер документа пережил смену статуса");

        Log("Проведение подтверждено: остаток 7, движений " + movements.Count
            + ", номер " + stored.Number + " на месте.");
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
