using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// «Ядерные тесты.Документы»: валидный документ (склад заполнен) проходит OnBeforePost —
// статус меняется на Posted и регистр двигается как обычно.
public class DocumentBeforePostAllowsTest : IntegrationTestScriptBase
{
    [IntegrationTest("Документы: событие пропускает валидное проведение")]
    public async Task ValidPostingPassesEvent()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();

        var docId = await CreateDocAsync(warehouse, item, 4m, 400m, DateTime.UtcNow, "Receipt");
        await Db.ChangeStatusAsync("TBStockDoc", docId, "Posted");

        var header = await Db.GetAsync("TBStockDoc", docId);
        Assert.IsNotNull(header, "документ читается");
        Assert.AreEqual("Posted", header!["StatusValue"], "статус сменился на Posted — событие пропустило проведение");

        var balances = await Db.QueryBalancesAsync("TBStock", "[Warehouse] = '" + warehouse + "' AND [Item] = '" + item + "'");
        Assert.AreEqual(1, balances.Count, "одна строка остатка по паре склад/номенклатура");
        Assert.AreEqual(4m, Convert.ToDecimal(balances[0]["Quantity"]), "остаток равен количеству строки");
        Log("Валидное проведение прошло: статус Posted, остаток 4.");
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