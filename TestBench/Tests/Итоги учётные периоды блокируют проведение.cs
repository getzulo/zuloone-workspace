using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы (TBStockDoc, TBLinesTablePartRow). Тест-скрипты НЕ
// получают это пространство имён глобальным using'ом.
using ZuloOne.Runtime.Generated;

// «Ядерные тесты.Итоги»: учётные периоды (MIQS AccountingPeriodManager).
// Закрытый период — жёсткая граница: движения с датой не позже её не проводятся
// и не распроводятся никем. Аудит-период — мягкая: требует права PostInAudit
// (тест-раннер работает без пользователя — права нет, проведение отклоняется).
//
// И документы, и переходы идут через IDocumentManager: статус — это присвоение
// StatusValue плюс сохранение, которое менеджер отдаёт движку. На харнессе
// остаётся только установка границ периодов: это запись метаданных
// (MetaAccountingPeriod) в обход проверки прав — тестовая ручка, менеджера у
// неё нет по определению.
public class TbAccountingPeriodsTest : IntegrationTestScriptBase
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    [IntegrationTest("учётные периоды блокируют проведение")]
    public async Task PeriodsGatePosting()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();
        var boundary = new DateTime(2026, 3, 31);

        // До установки границ документ внутри будущего периода проводится свободно.
        var early = await CreateDocAsync(warehouse, item, new DateTime(2026, 3, 15));
        await SetStatusAsync(early, "Posted");

        // 1. Закрытый период: новый документ с датой внутри границы не проводится.
        await Db.SetAccountingPeriodsAsync(boundary, null);
        var inside = await CreateDocAsync(warehouse, item, new DateTime(2026, 3, 20));
        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SetStatusAsync(inside, "Posted"),
            "проведение в закрытый период должно отклоняться");
        Assert.IsTrue(ex1.Message.Contains("CLOSED"), "ошибка называет закрытый период: {0}", ex1.Message);

        // 2. Уже проведённый документ внутри закрытого периода не распроводится.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SetStatusAsync(early, "Reverted"),
            "распроведение из закрытого периода должно отклоняться");

        // 3. Документ с датой ПОСЛЕ границы проводится свободно.
        var after = await CreateDocAsync(warehouse, item, new DateTime(2026, 4, 5));
        await SetStatusAsync(after, "Posted");

        // 4. Аудит-период: без права PostInAudit проведение тоже отклоняется.
        await Db.SetAccountingPeriodsAsync(null, boundary);
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SetStatusAsync(inside, "Posted"),
            "проведение в аудит-период без права должно отклоняться");
        Assert.IsTrue(ex2.Message.Contains("AUDIT"), "ошибка называет аудит-период: {0}", ex2.Message);

        // 5. Очистка границ восстанавливает и проведение, и распроведение.
        await Db.SetAccountingPeriodsAsync(null, null);
        await SetStatusAsync(inside, "Posted");
        await SetStatusAsync(early, "Reverted");
        Log("Границы сняты — документ проведён, ранний документ распроведён.");
    }

    // Переход статуса — присвоение плюс сохранение; отклонённый переход бросает
    // ровно то же исключение, что и движок, менеджер его не заворачивает.
    private static Task SetStatusAsync(TBStockDoc doc, string status)
    {
        doc.StatusValue = status;
        return DocumentManager.SaveDocumentAsync(doc);
    }

    private static async Task<TBStockDoc> CreateDocAsync(Guid warehouse, Guid item, DateTime date)
    {
        var doc = await DocumentManager.NewDocumentAsync<TBStockDoc>(TBStockDoc.Subtypes.Receipt);
        doc.Warehouse = warehouse;
        doc.DocumentDate = date;
        doc.Items.Add(new TBLinesTablePartRow { Item = item, Quantity = 5m, Amount = 500m });
        await DocumentManager.SaveDocumentAsync(doc);
        return doc;
    }
}
