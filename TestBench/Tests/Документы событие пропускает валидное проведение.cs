using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы (TBStockDoc, TBLinesTablePartRow) — тестовым скриптам
// это пространство имён глобальным using НЕ выдаётся.
using ZuloOne.Runtime.Generated;

// «Ядерные тесты.Документы»: валидный документ (склад заполнен) проходит OnBeforePost —
// статус меняется на Posted и регистр двигается как обычно.
public class DocumentBeforePostAllowsTest : IntegrationTestScriptBase
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("Документы: событие пропускает валидное проведение")]
    public async Task ValidPostingPassesEvent()
    {
        var warehouse = Db.NewId(); // Db.NewId() — законный остаток: генерация id, не доступ к данным.
        var item = Db.NewId();

        var doc = await NewDocAsync(warehouse, item, 4m, 400m, DateTime.UtcNow, TBStockDoc.Subtypes.Receipt);
        var filter = "[Warehouse] = '" + warehouse + "' AND [Item] = '" + item + "'";

        // Состояние ДО перехода: черновик движений не порождает. Без этой проверки
        // утверждения ниже проходят и тогда, когда документ провёлся сам на
        // сохранении, — и тест ничего не доказывает про переход.
        Assert.AreEqual(0, (await TotalsManager.QueryBalancesAsync("TBStock", filter)).Count,
            "черновик не должен двигать регистр");

        // Проведение — это ПРИСВОЕНИЕ статуса плюс сохранение (TBStockDoc вешает
        // движения на статус Posted, а не на подтип). SaveDocumentAsync проводит
        // изменившийся StatusValue через движок — симметрично смене подтипа.
        doc.StatusValue = "Posted";
        await DocumentManager.SaveDocumentAsync(doc);

        var stored = await DocumentManager.GetDocumentAsync<TBStockDoc>(doc.MetaId);
        Assert.IsNotNull(stored, "документ читается");
        Assert.AreEqual("Posted", stored!.StatusValue, "статус сменился на Posted — событие пропустило проведение");

        var balances = await TotalsManager.QueryBalancesAsync("TBStock", filter);
        Assert.AreEqual(1, balances.Count, "одна строка остатка по паре склад/номенклатура");
        Assert.AreEqual(4m, Convert.ToDecimal(balances[0]["Quantity"]), "остаток равен количеству строки");
        Log("Валидное проведение прошло: статус Posted, остаток 4.");
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
