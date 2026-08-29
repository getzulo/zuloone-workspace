using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

public class TypedLinkProbe : IntegrationTestScriptBase
{
    [IntegrationTest("Типизированные записи линк-таблицы: save / get / replace")]
    public async Task TypedRoundTrip()
    {
        var links = GetService<ILinkTableManager>();
        var ua = Db.NewId(); var uah = Db.NewId(); var usd = Db.NewId();

        // сохранить типизированную запись (MIQS SaveRecord)
        var row = new LT_CountryCurrency { Country = ua, Currency = uah, IsPrimary = true };
        var id = await links.SaveRecordAsync(row);
        Assert.IsTrue(id != Guid.Empty && row.MetaId == id, "MetaId прописан на записи: {0}", id);

        // выборка стороны (MIQS GetRecords(keyName, keyValue))
        var rows = await links.GetRecordsAsync<LT_CountryCurrency>("Country", ua);
        Assert.IsTrue(rows.Count == 1 && rows[0].IsPrimary == true, "строк {0}", rows.Count);

        // replace-семантика: сторона становится ровно заданным набором
        await links.ReplaceRecordsAsync("Country", ua, new[]
        {
            new LT_CountryCurrency { Country = ua, Currency = usd, IsPrimary = false },
        });
        rows = await links.GetRecordsAsync<LT_CountryCurrency>("Country", ua);
        Assert.IsTrue(rows.Count == 1, "после replace строк {0}", rows.Count);
        Assert.IsTrue(rows[0].Currency == usd, "валюта заменена");

        // обновление существующей записи по MetaId
        rows[0].IsPrimary = true;
        await links.SaveRecordAsync(rows[0]);
        rows = await links.GetRecordsAsync<LT_CountryCurrency>("Country", ua);
        Assert.IsTrue(rows[0].IsPrimary == true, "апдейт применился");
    }
}
