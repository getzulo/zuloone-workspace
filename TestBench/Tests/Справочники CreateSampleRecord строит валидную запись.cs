using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// «Ядерные тесты.Справочники»: CreateSampleRecordAsync строит шаблонную запись
// с заполненными полями по умолчанию; запись успешно вставляется и загружается.
public class TbSampleRecordTest : IntegrationTestScriptBase
{
    [IntegrationTest("CreateSampleRecord строит валидную запись")]
    public async Task SampleRecordIsInsertable()
    {
        var sample = await Db.CreateSampleRecordAsync("TBWarehouse");
        sample["Name"] = "SAMPLE-" + Db.NewId().ToString("N").Substring(0, 8).ToUpperInvariant();
        var id = await Db.InsertAsync("TBWarehouse", sample);
        Assert.IsTrue(id != Guid.Empty, "сгенерированная запись успешно вставлена");
        var loaded = await Db.GetAsync("TBWarehouse", id);
        Assert.IsTrue(loaded != null, "вставленная запись загружается обратно");
        Log("CreateSampleRecord → Insert → Get прошли без ошибок, id=" + id + ".");
    }
}