using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// «Ядерные тесты.Справочники»: EventResult.Cancel из OnBeforeSave прерывает вставку —
// сохранение бросает InvalidOperationException, запись не появляется.
//
// Пишется через IDictionaryManager типизированной записью: обработчик OnBeforeSave
// должен срабатывать на той же двери, которой пользуется бизнес-код.
public class DictionaryBeforeSaveCancelTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    [IntegrationTest("Справочники: OnBeforeSave отменяет сохранение")]
    public async Task BeforeSaveCancelsForbiddenName()
    {
        var before = await DictionaryManager.CountAsync<TBWarehouse>();

        var warehouse = DictionaryManager.NewRecord<TBWarehouse>();
        warehouse.Name = "FORBIDDEN";

        var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => DictionaryManager.SaveRecordAsync(warehouse),
            "сохранение с именем FORBIDDEN должно быть отклонено обработчиком");
        Assert.IsTrue(ex.Message.Contains("forbidden"), "причина отказа приходит из обработчика: {0}", ex.Message);

        var after = await DictionaryManager.CountAsync<TBWarehouse>();
        Assert.AreEqual(before, after, "количество записей TBWarehouse не изменилось после отказа");
        Log("Отмена подтверждена: '" + ex.Message + "', записей было/стало " + before + "/" + after + ".");
    }
}