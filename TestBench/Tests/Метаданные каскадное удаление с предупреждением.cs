// «Ядерные тесты.Метаданные»: удаление метаданных с полным каскадом (MIQS
// dependent-object warning) — превью импакта, блокеры входящих ссылок,
// снос физической таблицы вместе с объектом.
public partial class TbMetadataDeleteTests
{
    [IntegrationTest("импакт перечисляет зависимости и блокеры")]
    public async Task ImpactListsDependents()
    {
        var dictId = await WarehouseIdAsync();
        var impact = await Db.GetDeleteImpactAsync("Dictionary", dictId);
        Assert.IsTrue(impact.Blockers.Any(b => b.Contains("TBWarehouseRef")),
            "блокер — ссылочный EDT: {0}", string.Join("; ", impact.Blockers));
        Assert.IsTrue(impact.Cascade.Any(c => c.Contains("TBWarehouseInfo")),
            "каскад — команда справочника: {0}", string.Join("; ", impact.Cascade));
        Assert.IsTrue(impact.Cascade.Any(c => c.Contains("TBWarehouseEvents")),
            "каскад — скрипт обработчика событий");
        Assert.IsTrue(impact.Tables.Contains("TBWarehouse"), "каскад — физическая таблица");
        Log("Импакт: " + impact.Blockers.Count + " блокеров, " + impact.Cascade.Count + " зависимостей, таблиц: " + impact.Tables.Count + ".");
    }

    [IntegrationTest("удаление с входящими ссылками отклоняется")]
    public async Task BlockedDeleteRefused()
    {
        var dictId = await WarehouseIdAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Db.CascadeDeleteMetadataAsync("Dictionary", dictId),
            "на TBWarehouse ссылается EDT — удаление должно блокироваться");
        var still = await Db.GetDeleteImpactAsync("Dictionary", dictId);
        Assert.IsTrue(still.Blockers.Count > 0, "словарь жив и по-прежнему заблокирован");
    }

    [IntegrationTest("каскад уносит объект и его таблицу")]
    public async Task CascadeDeletesFreshDictionary()
    {
        var id = await Db.CreateDictionaryAsync("TBDelSandbox", new Dictionary<string, string> { ["Note"] = "String" });
        await Db.SyncSchemaAsync();
        await Db.InsertAsync("TBDelSandbox", new Dictionary<string, object?> { ["Note"] = "x" });

        var impact = await Db.GetDeleteImpactAsync("Dictionary", id);
        Assert.IsTrue(impact.Tables.Contains("TBDelSandbox"), "импакт видит физическую таблицу");
        Assert.AreEqual(1L, impact.DataRows, "и её строки данных");

        await Db.CascadeDeleteMetadataAsync("Dictionary", id);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Db.GetDeleteImpactAsync("Dictionary", id),
            "метаданные словаря удалены");
        Log("Каскад удалил TBDelSandbox вместе с таблицей и данными.");
    }

    private async Task<Guid> WarehouseIdAsync()
    {
        var rows = await Db.QueryAsync("MetaDictionaries", "[Name] = 'TBWarehouse'");
        Assert.AreEqual(1, rows.Count, "стендовый TBWarehouse существует");
        return (Guid)rows[0]["MetaId"]!;
    }
}