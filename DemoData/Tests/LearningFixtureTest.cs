using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;

public class LearningFixtureTest : IntegrationTestScriptBase
{
    [IntegrationTest("Урок начала работы находит свой товар, группу и две ячейки")]
    public async Task GettingStartedRowsExistOnce()
    {
        await Apply("Common/catalogs");
        await Apply("Organization/division-types");
        await Apply("Inventory/store-cell-types");
        await Apply("DemoData/learn-getting-started");
        await Apply("DemoData/learn-getting-started");

        var dictionaries = GetService<IDictionaryManager>();
        var items = await dictionaries.GetRecordsAsync<Item>("Name = 'Milk Chocolate 90g'");
        Assert.AreEqual(1, items.Count, "повторная заливка не плодит товар");
        var item = items[0];
        Assert.IsTrue((item.Barcode ?? "").StartsWith("482000"), "штрихкод урока");

        var group = await dictionaries.GetRecordAsync<ItemGroup>(item.ItemGroup);
        Assert.AreEqual("CONF", group!.Code);
        Assert.AreEqual("Confectionery", group.Name);

        var unit = await dictionaries.GetRecordAsync<UnitOfMeasure>(item.UnitOfMeasure);
        Assert.AreEqual("PCS", unit!.Code);

        var brand = await dictionaries.GetRecordAsync<Brand>(item.Brand);
        Assert.AreEqual("Roshen", brand!.Name);

        var stores = await dictionaries.GetRecordsAsync<Store>("Name = 'Learning Warehouse'");
        Assert.AreEqual(1, stores.Count, "учебный склад один");
        var storeId = stores[0].MetaId;
        await CellOnce(dictionaries, storeId, "Receiving dock", "DOCK-01-01");
        await CellOnce(dictionaries, storeId, "Ambient storage", "A-01-01");
    }

    private static async Task CellOnce(IDictionaryManager dictionaries, Guid storeId, string zoneName, string cellName)
    {
        var zones = (await dictionaries.GetRecordsAsync<StoreZone>($"Name = '{zoneName}'"))
            .Where(zone => zone.Store == storeId)
            .ToList();
        Assert.AreEqual(1, zones.Count, zoneName);
        var cells = (await dictionaries.GetRecordsAsync<StoreCell>($"Name = '{cellName}'"))
            .Where(cell => cell.StoreZone == zones[0].MetaId)
            .ToList();
        Assert.AreEqual(1, cells.Count, cellName);
    }

    private static async Task Apply(string id)
    {
        var result = await GetService<IDataPackageService>().ApplyAsync(id);
        Assert.IsTrue(result.Ok, id + ": " + string.Join("; ", result.Issues));
    }
}
