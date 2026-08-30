using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы (TBStockDoc, TBLinesTablePartRow). Тест-скрипты НЕ
// получают это пространство имён глобальным using'ом.
using ZuloOne.Runtime.Generated;

// «Ядерные тесты. Итоги»: провести → снять → провести снова не задваивает остаток.
//
// Документ строится типизированно через IDocumentManager, остаток и движения
// читаются через ITotalsManager. Переход по статусу — тоже менеджер: присвоение
// StatusValue плюс SaveDocumentAsync, который проводит изменившийся статус через
// движок (симметрично смене подтипа).
public class TotalsRepostIdempotenceTest : IntegrationTestScriptBase
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("Standard: повторное проведение идемпотентно")]
    public async Task RepostIsIdempotent()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();

        var docId = await CreateDocAsync(warehouse, item, 7m, 700m, DateTime.UtcNow, TBStockDoc.Subtypes.Receipt);

        // Номер выдаётся последовательностью при вставке: запоминаем его, чтобы
        // ниже убедиться, что три пересохранения подряд его не потеряли.
        var created = await DocumentManager.GetDocumentAsync<TBStockDoc>(docId);
        Assert.IsNotNull(created, "документ читается после создания");
        var number = created!.Number;
        Assert.IsTrue(!string.IsNullOrWhiteSpace(number), "последовательность выдала номер документа");

        // Снимок ДО перехода: пока документ не проведён, регистр пуст — иначе
        // проверки ниже проходят даже когда проведение ничего не сделало.
        Assert.AreEqual(0m, await StockQtyAsync(warehouse, item), "до проведения: остаток 0");

        await SetStatusAsync(docId, "Posted");
        Assert.AreEqual(7m, await StockQtyAsync(warehouse, item), "первое проведение: остаток 7");

        await SetStatusAsync(docId, "Reverted");
        Assert.AreEqual(0m, await StockQtyAsync(warehouse, item), "после снятия: остаток 0");

        await SetStatusAsync(docId, "Posted");
        Assert.AreEqual(7m, await StockQtyAsync(warehouse, item), "повторное проведение: ровно 7, без задвоения");

        var movements = await TotalsManager.QueryMovementsAsync("TBStock", "[DocumentMetaId] = '" + docId + "'");
        Assert.AreEqual(1, movements.Count, "после перепроведения — ровно одно движение");

        var reread = await DocumentManager.GetDocumentAsync<TBStockDoc>(docId);
        Assert.AreEqual(number, reread!.Number, "номер пережил три перехода по статусу");
        Log("Идемпотентность подтверждена: остаток 7, движение одно, номер " + number + ".");
    }

    // Переход по статусу: документ известен только по id, поэтому читаем его
    // менеджером, присваиваем StatusValue и сохраняем.
    private async Task SetStatusAsync(Guid docId, string status)
    {
        var doc = await DocumentManager.GetDocumentAsync<TBStockDoc>(docId);
        Assert.IsNotNull(doc, "документ читается перед сменой статуса");
        doc!.StatusValue = status;
        await DocumentManager.SaveDocumentAsync(doc);
    }

    // Срез регистра как число: менеджер сам возвращает 0, когда строки ещё нет.
    private Task<decimal> StockQtyAsync(Guid warehouse, Guid item)
        => TotalsManager.GetBalanceAsync("TBStock", "Quantity",
            new Dictionary<string, object?> { ["Warehouse"] = warehouse, ["Item"] = item });

    private async Task<Guid> CreateDocAsync(Guid warehouse, Guid item, decimal qty, decimal amount, DateTime date, string subtype)
    {
        var doc = await DocumentManager.NewDocumentAsync<TBStockDoc>(subtype);
        doc.Warehouse = warehouse;
        doc.DocumentDate = date;
        doc.Items.Add(new TBLinesTablePartRow { Item = item, Quantity = qty, Amount = amount });
        await DocumentManager.SaveDocumentAsync(doc);
        return doc.MetaId;
    }
}
