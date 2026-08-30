using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы (TBStockDoc, TBLinesTablePartRow, TBStockDoc.Subtypes) —
// тестовым скриптам это пространство имён глобальным using НЕ выдаётся.
using ZuloOne.Runtime.Generated;

// «Ядерные тесты.Документы»: OnBeforePost отклоняет проведение без склада.
// DocumentPostingService бросает ДО смены статуса, поэтому документ остаётся Draft
// и движений не появляется.
//
// Документ строится типизированно (NewDocumentAsync → строки → SaveDocumentAsync).
public class DocumentBeforePostBlocksTest : IntegrationTestScriptBase
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("Документы: OnBeforePost блокирует проведение")]
    public async Task BeforePostBlocksPostingWithoutWarehouse()
    {
        var item = Db.NewId(); // Db.NewId() — законный остаток: генерация id, не доступ к данным.
        var doc = await NewDocAsync(Guid.Empty, item, 3m, 300m, DateTime.UtcNow, TBStockDoc.Subtypes.Receipt);

        // Проведение — присвоение статуса плюс сохранение: SaveDocumentAsync
        // проводит изменившийся StatusValue через движок. Обработчик OnBeforePost
        // отклоняет переход броском — до того, как статус будет записан.
        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            async () =>
            {
                doc.StatusValue = "Posted";
                await DocumentManager.SaveDocumentAsync(doc);
            },
            "проведение без склада должно быть отклонено обработчиком OnBeforePost");
        Assert.IsTrue(ex.Message.Contains("Warehouse is required"), "причина отказа приходит из обработчика: {0}", ex.Message);

        var stored = await DocumentManager.GetDocumentAsync<TBStockDoc>(doc.MetaId);
        Assert.IsNotNull(stored, "документ сохранён");
        Assert.AreEqual("Draft", stored!.StatusValue, "статус остался Draft — проведение не состоялось");

        var movements = await TotalsManager.QueryMovementsAsync("TBStock", "[DocumentMetaId] = '" + doc.MetaId + "'");
        Assert.AreEqual(0, movements.Count, "движений отклонённого проведения нет");
        Log("Блокировка подтверждена: статус Draft, движений нет.");
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
