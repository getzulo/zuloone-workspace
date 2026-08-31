// «Ядерные тесты.Метаданные»: удаление метаданных с полным каскадом (MIQS
// dependent-object warning) — превью импакта, блокеры входящих ссылок,
// снос физической таблицы вместе с объектом.
//
// Предмет теста — ПЛАТФОРМЕННАЯ операция удаления метаданных, менеджера у неё
// нет: GetDeleteImpactAsync / CascadeDeleteMetadataAsync / CreateDictionaryAsync
// / SyncSchemaAsync остаются на Db осознанно. Всё остальное переведено на
// нормальные двери: словарь ищется через IMetadataService, а запись песочницы
// пишется именованной поверхностью IDictionaryManager.
public partial class TbMetadataDeleteTests
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

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
        // Словарь заведён ПРЯМО СЕЙЧАС, сгенерированного класса под него нет —
        // поэтому именованная поверхность менеджера, а не типизированная.
        await DictionaryManager.SaveRecordAsync("TBDelSandbox", new Dictionary<string, object?> { ["Note"] = "x" });

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
        // Справочник ищется через IMetadataService: Meta*-таблицы — это хранилище,
        // а сервис метаданных — та дверь, которой пользуется сама платформа.
        var found = (await GetService<IMetadataService>().GetAllDictionariesAsync())
            .Where(d => string.Equals(d.Name, "TBWarehouse", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.AreEqual(1, found.Count, "стендовый TBWarehouse существует");
        return found[0].MetaId;
    }
}
