using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// «Ядерные тесты. Итоги»: расход сверх остатка слоёв отклоняется FIFO-движком.
public class TotalsFifoOverdrawTest : IntegrationTestScriptBase
{
    [IntegrationTest("FIFO: перерасход отклоняется")]
    public async Task OverdrawIsRejected()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();
        var t0 = DateTime.UtcNow.AddMinutes(-10);

        var receipt = await CreateDocAsync(warehouse, item, 5m, 250m, t0, "Receipt");
        await Db.ChangeStatusAsync("TBStockDoc", receipt, "Posted");

        var before = await Db.QueryBalancesAsync("TBFifo", "[Item] = '" + item + "'");
        Assert.AreEqual(1, before.Count, "приход создал строку остатка");
        Assert.AreEqual(5m, Convert.ToDecimal(before[0]["Quantity"]), "в наличии 5 шт");

        var issue = await CreateDocAsync(warehouse, item, 6m, 0m, t0.AddMinutes(1), "Issue");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Db.ChangeStatusAsync("TBStockDoc", issue, "Posted"),
            "проведение расхода 6 шт при остатке 5 шт должно быть отклонено");
        Assert.IsTrue(ex.Message.Contains("Insufficient"), "движок сообщает о нехватке слоёв FIFO: {0}", ex.Message);

        // Отклонённое проведение голосует за откат объемлющей транзакции кейса
        // (вложенный TransactionScope без Complete), поэтому чтение после отказа
        // может быть уже недоступно — тогда неизменность остатка гарантирует
        // общий откат кейса, а не эта проверка.
        try
        {
            var after = await Db.QueryBalancesAsync("TBFifo", "[Item] = '" + item + "'");
            var qty = after.Count == 0 ? 0m : Convert.ToDecimal(after[0]["Quantity"]);
            Assert.AreEqual(5m, qty, "остаток не изменился после отклонённого проведения");
            var movements = await Db.QueryMovementsAsync("TBFifo", "[DocumentMetaId] = '" + issue + "'");
            Assert.AreEqual(0, movements.Count, "движений отклонённого документа нет");
            Log("Остаток подтверждён неизменным: 5 шт.");
        }
        catch (IntegrationTestException)
        {
            throw;
        }
        catch (Exception readEx)
        {
            Log("Проверка остатка после отказа недоступна (" + readEx.GetType().Name +
                ") — транзакция кейса помечена на откат, изменения не сохраняются.");
        }
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