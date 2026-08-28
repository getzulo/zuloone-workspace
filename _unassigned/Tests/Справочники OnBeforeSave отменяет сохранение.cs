using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// «Ядерные тесты.Справочники»: EventResult.Cancel из OnBeforeSave прерывает вставку —
// DataService бросает InvalidOperationException, запись не появляется.
public class DictionaryBeforeSaveCancelTest : IntegrationTestScriptBase
{
    [IntegrationTest("Справочники: OnBeforeSave отменяет сохранение")]
    public async Task BeforeSaveCancelsForbiddenName()
    {
        var before = await Db.CountAsync("TBWarehouse");

        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = "FORBIDDEN" }),
            "вставка с именем FORBIDDEN должна быть отклонена обработчиком");
        Assert.IsTrue(ex.Message.Contains("forbidden"), "причина отказа приходит из обработчика: {0}", ex.Message);

        var after = await Db.CountAsync("TBWarehouse");
        Assert.AreEqual(before, after, "количество записей TBWarehouse не изменилось после отказа");
        Log("Отмена подтверждена: '" + ex.Message + "', записей было/стало " + before + "/" + after + ".");
    }
}