using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Auto-generated CRUD test for dictionary "ChartOfAccounts".
// Sample records are built at runtime (required fields + resolved references).
public class AutoTest_ChartOfAccounts : IntegrationTestScriptBase
{
    [IntegrationTest("Create ChartOfAccounts record")]
    public async Task Create()
    {
        var id = await Db.InsertAsync("ChartOfAccounts", await Db.CreateSampleRecordAsync("ChartOfAccounts"));
        Assert.AreNotEqual(System.Guid.Empty, id, "expected a real id");
        Assert.IsNotNull(await Db.GetAsync("ChartOfAccounts", id), "record should be readable after insert");
    }

    [IntegrationTest("Update ChartOfAccounts record")]
    public async Task Update()
    {
        var id = await Db.InsertAsync("ChartOfAccounts", await Db.CreateSampleRecordAsync("ChartOfAccounts"));
        var ok = await Db.UpdateAsync("ChartOfAccounts", id, new Dictionary<string, object?> { ["Name"] = "Auto test updated" });
        Assert.IsTrue(ok, "update should succeed");
    }

    [IntegrationTest("Delete ChartOfAccounts record")]
    public async Task Delete()
    {
        var id = await Db.InsertAsync("ChartOfAccounts", await Db.CreateSampleRecordAsync("ChartOfAccounts"));
        var ok = await Db.DeleteAsync("ChartOfAccounts", id);
        Assert.IsTrue(ok, "delete should succeed");
        Assert.IsNull(await Db.GetAsync("ChartOfAccounts", id), "record should be gone after delete");
    }
}