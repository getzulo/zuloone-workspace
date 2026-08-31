using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Обязательно: тестовым скриптам этот namespace НЕ выдаётся глобальным using,
// без него генерированные классы (TBWarehouse) просто не находятся.
using ZuloOne.Runtime.Generated;

// «Ядерные тесты.Справочники»: OnBeforeSave обработчика TBWarehouse мутирует запись
// (имя в нижнем регистре переводится в верхний), и мутация сохраняется в БД.
//
// Записи создаются и читаются ЧЕРЕЗ IDictionaryManager типизированно — той же
// дверью, что и бизнес-код: событие обязано срабатывать на этом пути, а не только
// на служебном.
public class DictionaryBeforeSaveMutationTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    [IntegrationTest("Справочники: OnBeforeSave меняет запись")]
    public async Task BeforeSaveUppercasesName()
    {
        var name = "test warehouse " + Guid.NewGuid().ToString("N").Substring(0, 8);

        var warehouse = DictionaryManager.NewRecord<TBWarehouse>();
        warehouse.Name = name;
        warehouse = await DictionaryManager.SaveRecordAsync(warehouse);
        Assert.AreNotEqual(Guid.Empty, warehouse.MetaId, "сохранение вернуло настоящий идентификатор");

        var stored = await DictionaryManager.GetRecordAsync<TBWarehouse>(warehouse.MetaId);
        Assert.IsNotNull(stored, "запись читается после сохранения");
        Assert.AreEqual(name.ToUpperInvariant(), stored!.Name,
            "OnBeforeSave перевёл имя в верхний регистр — событие сработало и изменило запись");
        Log("Событие сработало: '" + name + "' → '" + stored.Name + "'.");
    }
}
