using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы сущностей (TBWarehouse). Тестовые скрипты НЕ получают
// это пространство имён глобальным using — без него класс просто не находится.
using ZuloOne.Runtime.Generated;

// «Ядерные тесты.Справочники»: CreateSampleRecordAsync строит шаблонную запись
// с заполненными обязательными полями; запись сохраняется МЕНЕДЖЕРОМ и читается
// обратно как типизированная сущность.
public class TbSampleRecordTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    [IntegrationTest("CreateSampleRecord строит валидную запись")]
    public async Task SampleRecordIsInsertable()
    {
        // Db остаётся ровно на том, чего у менеджера нет: построение образцовой
        // записи по метаданным (аналог MIQS CreateSampleRecord) и генерация id.
        var sample = await Db.CreateSampleRecordAsync("TBWarehouse");
        sample["Name"] = "SAMPLE-" + Db.NewId().ToString("N").Substring(0, 8).ToUpperInvariant();

        // Сохранение — уже через менеджер: образец приходит МЕШКОМ полей, поэтому
        // берём его by-name дверь (та же самая дверь, не обход типизированной).
        var id = await DictionaryManager.SaveRecordAsync("TBWarehouse", new Dictionary<string, object?>(sample));
        Assert.IsTrue(id != Guid.Empty, "сгенерированная запись успешно вставлена");

        // Обратно читаем ТИПИЗИРОВАННО: смысл теста в том, что образец — валидная
        // запись справочника, а не просто строка, которую стерпела таблица.
        var loaded = await DictionaryManager.GetRecordAsync<TBWarehouse>(id);
        Assert.IsTrue(loaded != null, "вставленная запись загружается обратно");
        Log("CreateSampleRecord → SaveRecord → GetRecord прошли без ошибок, id=" + id + ".");
    }
}
