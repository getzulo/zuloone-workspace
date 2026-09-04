using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Рабочие данные должны переживать включение EnforceWarehouseTasks: у склада
// появляются ячейки трёх ролей, типы с именем роли получают Purpose, повтор
// ничего не плодит. Умолчание флага по-прежнему выкл. — иначе фикстуры с
// выдуманными id ячеек падают. Здесь проверяется подготовка, не сам флаг.
public class WarehouseYardTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IStoreCellService Cells => GetService<IStoreCellService>();

    private async Task<Guid> NewStoreAsync()
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = "EUR";
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = "DE";
        country.CodeISO3 = "DEU";
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "ACME GmbH";
        legalEntity.RegistrationNumber = $"REG-YD-{Db.NewId():N}"[..18];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"YD-{Db.NewId():N}"[..12];
        divisionType.Name = "Warehouse";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Main";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var store = DictionaryManager.NewRecord<Store>();
        store.Name = "Central";
        store.Division = division.MetaId;
        store.IsSimple = true;
        store = await DictionaryManager.SaveRecordAsync(store);
        return store.MetaId;
    }

    private static async Task SetDisciplineAsync(bool on)
    {
        var manager = GetService<IDictionaryManager<InventorySettings>>();
        var rows = await manager.GetRecordsAsync("1 = 1");
        var settings = rows.Count > 0 ? rows[0] : await manager.NewRecordAsync();
        settings.EnforceWarehouseTasks = on;
        await manager.SaveRecordAsync(settings);
    }

    [IntegrationTest("Без дисциплины новый склад двор не плодит")]
    public async Task DisciplineOffLeavesStoreBare()
    {
        await SetDisciplineAsync(false);
        var store = await NewStoreAsync();

        Assert.IsTrue(await Cells.GetCellByPurposeAsync(store, StoreCellPurpose.Receiving) == null,
            "без флага приёмка не появляется сама");
        Assert.IsTrue((await Cells.GetCellsOfStoreAsync(store)).Count == 0,
            "ячеек нет, факт {0}", (await Cells.GetCellsOfStoreAsync(store)).Count);
    }

    [IntegrationTest("Включение дисциплины дособирает три роли и не плодит повтор")]
    public async Task EnablingDisciplineBuildsYardOnce()
    {
        await SetDisciplineAsync(false);
        var store = await NewStoreAsync();

        await SetDisciplineAsync(true);

        var receiving = await Cells.GetCellByPurposeAsync(store, StoreCellPurpose.Receiving);
        var storage = await Cells.GetCellByPurposeAsync(store, StoreCellPurpose.Storage);
        var picking = await Cells.GetCellByPurposeAsync(store, StoreCellPurpose.Picking);
        Assert.IsTrue(receiving != null && storage != null && picking != null,
            "после флага есть приёмка, хранение и отбор");
        Assert.IsTrue(receiving != storage && storage != picking && receiving != picking,
            "три разные ячейки");

        var again = await Cells.PrepareAllYardsAsync();
        Assert.IsTrue(again == 0, "повтор не создаёт ячеек, факт {0}", again);
        Assert.IsTrue((await Cells.GetCellsOfStoreAsync(store)).Count == 3,
            "ячеек по-прежнему три, факт {0}", (await Cells.GetCellsOfStoreAsync(store)).Count);
    }

    [IntegrationTest("Новый склад при включённой дисциплине сразу с двором")]
    public async Task NewStoreWhileOnGetsYard()
    {
        await SetDisciplineAsync(true);
        var store = await NewStoreAsync();

        Assert.IsTrue(await Cells.GetCellByPurposeAsync(store, StoreCellPurpose.Picking) != null,
            "склад, заведённый под флагом, уже с отбором");
        Assert.IsTrue((await Cells.GetCellsOfStoreAsync(store)).Count == 3,
            "три ячейки, факт {0}", (await Cells.GetCellsOfStoreAsync(store)).Count);
    }

    [IntegrationTest("Тип с именем Receiving без Purpose получает назначение при подготовке")]
    public async Task PrepareInfersPurposeFromTypeName()
    {
        await SetDisciplineAsync(false);
        await NewStoreAsync();

        var type = DictionaryManager.NewRecord<StoreCellType>();
        type.Code = $"RCV-{Db.NewId():N}"[..12];
        type.Name = "Receiving";
        type = await DictionaryManager.SaveRecordAsync(type);
        Assert.IsTrue(type.Purpose == StoreCellPurpose.Unspecified,
            "до подготовки назначение пустое, факт {0}", type.Purpose);

        await Cells.PrepareAllYardsAsync();
        var reloaded = await GetService<IDictionaryManager<StoreCellType>>().GetRecordAsync(type.MetaId);
        Assert.IsTrue(reloaded!.Purpose == StoreCellPurpose.Receiving,
            "имя Receiving стало назначением приёмки, факт {0}", reloaded.Purpose);
    }
}
