// «Ядерные тесты.Связи»: связи документов (MIQS PARENT_DOCUMENTS) — цепочка
// «документ создал документ» видна целиком как граф семьи из любого её узла.
//
// Связи живут на IDocumentManager (AddLinkAsync / RemoveLinkAsync /
// GetDocumentFamilyAsync) — та же дверь, которой пользуется бизнес-код;
// фасад Db лишь форматировал её рёбра в строки, что тест делает сам.
public partial class TbDocumentLinkTests
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    [IntegrationTest("цепочка A→B→C: семья видна из середины")]
    public async Task ChainFamilyFromMiddle()
    {
        var (a, b, c) = await CreateChainAsync();
        var edgesFromB = await FamilyEdgesAsync(b);
        Assert.AreEqual(2, edgesFromB.Count, "из середины видны оба ребра: {0}", string.Join("; ", edgesFromB));
        Assert.IsTrue(edgesFromB.Contains(a + "->" + b), "ребро A→B");
        Assert.IsTrue(edgesFromB.Contains(b + "->" + c), "ребро B→C");

        var edgesFromA = await FamilyEdgesAsync(a);
        Assert.AreEqual(2, edgesFromA.Count, "и из начала цепочки — та же семья");
        Log("Семья цепочки A→B→C собрана: 2 ребра из любого узла.");
    }

    [IntegrationTest("цикл C→A не вешает обход, RemoveLink убирает ребро")]
    public async Task CycleSafeAndRemove()
    {
        var (a, b, c) = await CreateChainAsync();
        await DocumentManager.AddLinkAsync(c, a); // замыкаем цикл
        var edges = await FamilyEdgesAsync(b);
        Assert.AreEqual(3, edges.Count, "цикл даёт три ребра и обход завершается: {0}", string.Join("; ", edges));

        await DocumentManager.RemoveLinkAsync(c, a);
        edges = await FamilyEdgesAsync(b);
        Assert.AreEqual(2, edges.Count, "после RemoveLink цикл разомкнут");
        Log("Cycle-safe обход и удаление ребра подтверждены.");
    }

    [IntegrationTest("самосвязь и несуществующий документ отклоняются")]
    public async Task ValidationGuards()
    {
        var (a, _, _) = await CreateChainAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DocumentManager.AddLinkAsync(a, a), "документ не может ссылаться сам на себя");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DocumentManager.AddLinkAsync(a, Guid.NewGuid()), "битый id потомка отклоняется");
        await Task.CompletedTask;
    }

    /// <summary>Рёбра семьи как «parentId->childId», отсортированные — форма, в
    /// которой их удобно сравнивать (ровно то, что делал фасад).</summary>
    private static async Task<List<string>> FamilyEdgesAsync(Guid documentId)
    {
        var family = await DocumentManager.GetDocumentFamilyAsync(documentId);
        return family.Edges.Select(e => e.ParentDocId + "->" + e.ChildDocId).OrderBy(x => x).ToList();
    }

    /// <summary>Three Receipt documents linked A→B→C (the bench TBStockDoc fixtures).</summary>
    private async Task<(Guid A, Guid B, Guid C)> CreateChainAsync()
    {
        var warehouse = DictionaryManager.NewRecord<TBWarehouse>();
        warehouse.Name = "LinkWh-" + Db.NewId().ToString("N").Substring(0, 6);
        warehouse = await DictionaryManager.SaveRecordAsync(warehouse);

        var item = DictionaryManager.NewRecord<TBItem>();
        item.WarehouseID = warehouse.MetaId;
        item = await DictionaryManager.SaveRecordAsync(item);

        var a = await CreateDocAsync(warehouse.MetaId, item.MetaId, 1m);
        var b = await CreateDocAsync(warehouse.MetaId, item.MetaId, 2m);
        var c = await CreateDocAsync(warehouse.MetaId, item.MetaId, 3m);
        await DocumentManager.AddLinkAsync(a, b);
        await DocumentManager.AddLinkAsync(b, c);
        return (a, b, c);
    }

    private async Task<Guid> CreateDocAsync(Guid warehouse, Guid item, decimal qty)
    {
        var doc = await DocumentManager.NewDocumentAsync<TBStockDoc>(TBStockDoc.Subtypes.Receipt);
        doc.Warehouse = warehouse;
        doc.DocumentDate = new DateTime(2026, 3, 1);
        doc.Items.Add(new TBLinesTablePartRow { Item = item, Quantity = qty, Amount = qty * 10m });
        await DocumentManager.SaveDocumentAsync(doc);
        return doc.MetaId;
    }
}
