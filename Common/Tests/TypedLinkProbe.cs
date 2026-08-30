using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы (LT_CountryCurrency). Тест-скрипты НЕ получают это
// пространство имён глобальным using'ом.
using ZuloOne.Runtime.Generated;

// Линк-таблица через менеджер и типизированные записи: save / get / replace.
public class TypedLinkProbe : IntegrationTestScriptBase
{
    // Менеджер — амбиентное свойство класса (MIQS держит менеджеры на базе
    // скрипта; сборка ZuloOne.Runtime не может назвать интерфейсы Core, поэтому
    // объявление живёт здесь).
    private static ILinkTableManager LinkTableManager => GetService<ILinkTableManager>();

    [IntegrationTest("Типизированные записи линк-таблицы: save / get / replace")]
    public async Task TypedRoundTrip()
    {
        // Db.NewId() остаётся: стороны связи — просто ключи, записи справочников
        // этой проверке не нужны.
        var ua = Db.NewId(); var uah = Db.NewId(); var usd = Db.NewId();

        // сохранить типизированную запись (MIQS SaveRecord)
        var row = new LT_CountryCurrency { Country = ua, Currency = uah, IsPrimary = true };
        var id = await LinkTableManager.SaveRecordAsync(row);
        Assert.IsTrue(id != Guid.Empty && row.MetaId == id, "MetaId прописан на записи: {0}", id);

        // выборка стороны (MIQS GetRecords(keyName, keyValue))
        var rows = await LinkTableManager.GetRecordsAsync<LT_CountryCurrency>("Country", ua);
        Assert.IsTrue(rows.Count == 1 && rows[0].IsPrimary == true, "строк {0}", rows.Count);

        // replace-семантика: сторона становится ровно заданным набором
        await LinkTableManager.ReplaceRecordsAsync("Country", ua, new[]
        {
            new LT_CountryCurrency { Country = ua, Currency = usd, IsPrimary = false },
        });
        rows = await LinkTableManager.GetRecordsAsync<LT_CountryCurrency>("Country", ua);
        Assert.IsTrue(rows.Count == 1, "после replace строк {0}", rows.Count);
        Assert.IsTrue(rows[0].Currency == usd, "валюта заменена");

        // обновление существующей записи по MetaId
        rows[0].IsPrimary = true;
        await LinkTableManager.SaveRecordAsync(rows[0]);
        rows = await LinkTableManager.GetRecordsAsync<LT_CountryCurrency>("Country", ua);
        Assert.IsTrue(rows[0].IsPrimary == true, "апдейт применился");
    }
}
