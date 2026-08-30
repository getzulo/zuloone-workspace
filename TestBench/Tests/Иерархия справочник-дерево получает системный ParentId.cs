// «Ядерные тесты.Иерархия»: IsHierarchical материализуется системным
// self-reference полем ParentId (легаси Dictionary.ParentPropertyID) — на нём
// строится форма-дерево; каскадное удаление не блокируется собственным EDT.
//
// ГРАНИЦА ПЕРЕПИСЫВАНИЯ НА МЕНЕДЖЕРЫ. Справочник здесь РОЖДАЕТСЯ ВНУТРИ теста,
// поэтому генерённого класса TBTreeSandbox* не существует и типизированная
// дверь (NewRecord<T>/GetRecordsAsync<T>) физически недоступна. Строки поэтому
// пишутся и читаются через ПО-ИМЕННУЮ поверхность IDictionaryManager — это тот
// же менеджер, просто его нетипизированная половина.
// На харнессе намеренно остаются три вещи, у которых менеджера в платформе нет
// вовсе, потому что предмет проверки — сама метадата и DDL, а не бизнес-данные:
//   * CreateDictionaryAsync / SyncSchemaAsync — конструктор метаданных и DDL;
//   * QueryAsync по MetaDictionaryProperties / MetaEDTs — прямая проба
//     платформенных таблиц метаданных (они не справочники, и звать над ними
//     менеджер справочников было бы враньём об их природе);
//   * GetDeleteImpactAsync / CascadeDeleteMetadataAsync — каскад метаданных.
public partial class TbHierarchyTests
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    [IntegrationTest("иерархический справочник получает системный ParentId")]
    public async Task ParentFieldMaterializes()
    {
        var dictId = await Db.CreateDictionaryAsync("TBTreeSandbox",
            new Dictionary<string, string> { ["Note"] = "String" }, isHierarchical: true);

        var parentProps = await Db.QueryAsync("MetaDictionaryProperties",
            "[DictionaryMetaId] = '" + dictId + "' AND [FieldName] = 'ParentId'");
        Assert.AreEqual(1, parentProps.Count, "системное свойство ParentId создано автоматически");

        var edts = await Db.QueryAsync("MetaEDTs", "[Name] = 'TBTreeSandboxParentRef'");
        Assert.AreEqual(1, edts.Count, "self-reference EDT создан");
        Log("ParentId + TBTreeSandboxParentRef материализованы.");
    }

    [IntegrationTest("дети выбираются фильтром по родителю")]
    public async Task ChildrenByParentFilter()
    {
        await Db.CreateDictionaryAsync("TBTreeSandbox2",
            new Dictionary<string, string> { ["Note"] = "String" }, isHierarchical: true);
        await Db.SyncSchemaAsync();

        var root = await NewNodeAsync("root", null);
        var childA = await NewNodeAsync("a", root);
        await NewNodeAsync("b", root);
        await NewNodeAsync("aa", childA);

        var children = await DictionaryManager.GetRecordsAsync("TBTreeSandbox2", "[ParentId] = '" + root + "'");
        Assert.AreEqual(2, children.Count, "у корня два прямых потомка");
        var roots = await DictionaryManager.GetRecordsAsync("TBTreeSandbox2", "[ParentId] IS NULL");
        Assert.AreEqual(1, roots.Count, "корневая запись одна");
        Log("Дерево из четырёх записей собирается фильтрами по ParentId.");
    }

    /// <summary>Узел дерева через менеджер справочников: класса у только что
    /// созданного справочника нет, поэтому поля идут мешком по имени.</summary>
    private static Task<System.Guid> NewNodeAsync(string note, System.Guid? parent)
        => DictionaryManager.SaveRecordAsync("TBTreeSandbox2",
            new Dictionary<string, object?> { ["Note"] = note, ["ParentId"] = parent });

    [IntegrationTest("каскадное удаление уносит собственный ParentRef EDT")]
    public async Task CascadeTakesSelfReferenceEdt()
    {
        var dictId = await Db.CreateDictionaryAsync("TBTreeSandbox3",
            new Dictionary<string, string> { ["Note"] = "String" }, isHierarchical: true);
        await Db.SyncSchemaAsync();

        var impact = await Db.GetDeleteImpactAsync("Dictionary", dictId);
        Assert.AreEqual(0, impact.Blockers.Count,
            "собственный self-reference EDT не блокирует удаление: {0}", string.Join("; ", impact.Blockers));
        Assert.IsTrue(impact.Cascade.Any(c => c.Contains("TBTreeSandbox3ParentRef")),
            "EDT в каскаде: {0}", string.Join("; ", impact.Cascade));

        await Db.CascadeDeleteMetadataAsync("Dictionary", dictId);
        var edts = await Db.QueryAsync("MetaEDTs", "[Name] = 'TBTreeSandbox3ParentRef'");
        Assert.AreEqual(0, edts.Count, "EDT удалён вместе со словарём");
        Log("Каскад унёс словарь, таблицу и ParentRef EDT.");
    }
}
