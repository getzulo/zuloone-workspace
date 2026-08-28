using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// «Ядерные тесты.Справочники»: OnBeforeSave обработчика TBWarehouse мутирует запись
// (имя в нижнем регистре переводится в верхний), и мутация сохраняется в БД.
public class DictionaryBeforeSaveMutationTest : IntegrationTestScriptBase
{
    [IntegrationTest("Справочники: OnBeforeSave меняет запись")]
    public async Task BeforeSaveUppercasesName()
    {
        var name = "test warehouse " + Guid.NewGuid().ToString("N").Substring(0, 8);
        var id = await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = name });
        Assert.AreNotEqual(Guid.Empty, id, "вставка вернула настоящий идентификатор");

        var row = await Db.GetAsync("TBWarehouse", id);
        Assert.IsNotNull(row, "запись читается после вставки");
        Assert.AreEqual(name.ToUpperInvariant(), row!["Name"],
            "OnBeforeSave перевёл имя в верхний регистр — событие сработало и изменило запись");
        Log("Событие сработало: '" + name + "' → '" + row["Name"] + "'.");
    }
}