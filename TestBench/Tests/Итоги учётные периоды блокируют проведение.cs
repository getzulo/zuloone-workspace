using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// «Ядерные тесты.Итоги»: учётные периоды (MIQS AccountingPeriodManager).
// Закрытый период — жёсткая граница: движения с датой не позже её не проводятся
// и не распроводятся никем. Аудит-период — мягкая: требует права PostInAudit
// (тест-раннер работает без пользователя — права нет, проведение отклоняется).
public class TbAccountingPeriodsTest : IntegrationTestScriptBase
{
    [IntegrationTest("учётные периоды блокируют проведение")]
    public async Task PeriodsGatePosting()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();
        var boundary = new DateTime(2026, 3, 31);

        // До установки границ документ внутри будущего периода проводится свободно.
        var early = await CreateDocAsync(warehouse, item, new DateTime(2026, 3, 15));
        await Db.ChangeStatusAsync("TBStockDoc", early, "Posted");

        // 1. Закрытый период: новый документ с датой внутри границы не проводится.
        await Db.SetAccountingPeriodsAsync(boundary, null);
        var inside = await CreateDocAsync(warehouse, item, new DateTime(2026, 3, 20));
        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Db.ChangeStatusAsync("TBStockDoc", inside, "Posted"),
            "проведение в закрытый период должно отклоняться");
        Assert.IsTrue(ex1.Message.Contains("CLOSED"), "ошибка называет закрытый период: {0}", ex1.Message);

        // 2. Уже проведённый документ внутри закрытого периода не распроводится.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Db.ChangeStatusAsync("TBStockDoc", early, "Reverted"),
            "распроведение из закрытого периода должно отклоняться");

        // 3. Документ с датой ПОСЛЕ границы проводится свободно.
        var after = await CreateDocAsync(warehouse, item, new DateTime(2026, 4, 5));
        await Db.ChangeStatusAsync("TBStockDoc", after, "Posted");

        // 4. Аудит-период: без права PostInAudit проведение тоже отклоняется.
        await Db.SetAccountingPeriodsAsync(null, boundary);
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Db.ChangeStatusAsync("TBStockDoc", inside, "Posted"),
            "проведение в аудит-период без права должно отклоняться");
        Assert.IsTrue(ex2.Message.Contains("AUDIT"), "ошибка называет аудит-период: {0}", ex2.Message);

        // 5. Очистка границ восстанавливает и проведение, и распроведение.
        await Db.SetAccountingPeriodsAsync(null, null);
        await Db.ChangeStatusAsync("TBStockDoc", inside, "Posted");
        await Db.ChangeStatusAsync("TBStockDoc", early, "Reverted");
        Log("Границы сняты — документ проведён, ранний документ распроведён.");
    }

    private Task<Guid> CreateDocAsync(Guid warehouse, Guid item, DateTime date)
        => Db.CreateDocumentAsync(
            "TBStockDoc",
            new Dictionary<string, object?> { ["Warehouse"] = warehouse, ["DocumentDate"] = date },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Items"] = new IDictionary<string, object?>[]
                {
                    new Dictionary<string, object?> { ["Item"] = item, ["Quantity"] = 5m, ["Amount"] = 500m },
                },
            },
            "Receipt");
}