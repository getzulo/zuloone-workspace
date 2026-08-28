using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// «Ядерные тесты.Документы»: OnBeforePost отклоняет проведение без склада.
// DocumentPostingService бросает ДО смены статуса, поэтому документ остаётся Draft
// и движений не появляется.
public class DocumentBeforePostBlocksTest : IntegrationTestScriptBase
{
    [IntegrationTest("Документы: OnBeforePost блокирует проведение")]
    public async Task BeforePostBlocksPostingWithoutWarehouse()
    {
        var item = Db.NewId();
        var docId = await CreateDocAsync(Guid.Empty, item, 3m, 300m, DateTime.UtcNow, "Receipt");

        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => Db.ChangeStatusAsync("TBStockDoc", docId, "Posted"),
            "проведение без склада должно быть отклонено обработчиком OnBeforePost");
        Assert.IsTrue(ex.Message.Contains("Warehouse is required"), "причина отказа приходит из обработчика: {0}", ex.Message);

        var header = await Db.GetAsync("TBStockDoc", docId);
        Assert.IsNotNull(header, "документ сохранён");
        Assert.AreEqual("Draft", header!["StatusValue"], "статус остался Draft — проведение не состоялось");

        var movements = await Db.QueryMovementsAsync("TBStock", "[DocumentMetaId] = '" + docId + "'");
        Assert.AreEqual(0, movements.Count, "движений отклонённого проведения нет");
        Log("Блокировка подтверждена: статус Draft, движений нет.");
    }

    private Task<Guid> CreateDocAsync(Guid warehouse, Guid item, decimal qty, decimal amount, DateTime date, string subtype)
        => Db.CreateDocumentAsync(
            "TBStockDoc",
            new Dictionary<string, object?> { ["Warehouse"] = warehouse, ["DocumentDate"] = date },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Items"] = new IDictionary<string, object?>[]
                {
                    new Dictionary<string, object?> { ["Item"] = item, ["Quantity"] = qty, ["Amount"] = amount },
                },
            },
            subtype);
}