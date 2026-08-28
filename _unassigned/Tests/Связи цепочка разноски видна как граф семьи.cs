// «Ядерные тесты.Связи»: связи документов (MIQS PARENT_DOCUMENTS) — цепочка
// «документ создал документ» видна целиком как граф семьи из любого её узла.
public partial class TbDocumentLinkTests
{
    [IntegrationTest("цепочка A→B→C: семья видна из середины")]
    public async Task ChainFamilyFromMiddle()
    {
        var (a, b, c) = await CreateChainAsync();
        var edgesFromB = await Db.GetDocumentFamilyEdgesAsync(b);
        Assert.AreEqual(2, edgesFromB.Count, "из середины видны оба ребра: {0}", string.Join("; ", edgesFromB));
        Assert.IsTrue(edgesFromB.Contains(a + "->" + b), "ребро A→B");
        Assert.IsTrue(edgesFromB.Contains(b + "->" + c), "ребро B→C");

        var edgesFromA = await Db.GetDocumentFamilyEdgesAsync(a);
        Assert.AreEqual(2, edgesFromA.Count, "и из начала цепочки — та же семья");
        Log("Семья цепочки A→B→C собрана: 2 ребра из любого узла.");
    }

    [IntegrationTest("цикл C→A не вешает обход, RemoveLink убирает ребро")]
    public async Task CycleSafeAndRemove()
    {
        var (a, b, c) = await CreateChainAsync();
        await Db.AddDocumentLinkAsync(c, a); // замыкаем цикл
        var edges = await Db.GetDocumentFamilyEdgesAsync(b);
        Assert.AreEqual(3, edges.Count, "цикл даёт три ребра и обход завершается: {0}", string.Join("; ", edges));

        await Db.RemoveDocumentLinkAsync(c, a);
        edges = await Db.GetDocumentFamilyEdgesAsync(b);
        Assert.AreEqual(2, edges.Count, "после RemoveLink цикл разомкнут");
        Log("Cycle-safe обход и удаление ребра подтверждены.");
    }

    [IntegrationTest("самосвязь и несуществующий документ отклоняются")]
    public async Task ValidationGuards()
    {
        var (a, _, _) = await CreateChainAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Db.AddDocumentLinkAsync(a, a), "документ не может ссылаться сам на себя");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Db.AddDocumentLinkAsync(a, Guid.NewGuid()), "битый id потомка отклоняется");
        await Task.CompletedTask;
    }

    /// <summary>Three Receipt documents linked A→B→C (the bench TBStockDoc fixtures).</summary>
    private async Task<(Guid A, Guid B, Guid C)> CreateChainAsync()
    {
        var warehouse = await Db.InsertAsync("TBWarehouse", new Dictionary<string, object?> { ["Name"] = "LinkWh-" + Db.NewId().ToString("N").Substring(0, 6) });
        var item = await Db.InsertAsync("TBItem", new Dictionary<string, object?> { ["WarehouseID"] = warehouse });
        var a = await CreateDocAsync(warehouse, item, 1m);
        var b = await CreateDocAsync(warehouse, item, 2m);
        var c = await CreateDocAsync(warehouse, item, 3m);
        await Db.AddDocumentLinkAsync(a, b);
        await Db.AddDocumentLinkAsync(b, c);
        return (a, b, c);
    }

    private Task<Guid> CreateDocAsync(Guid warehouse, Guid item, decimal qty)
        => Db.CreateDocumentAsync(
            "TBStockDoc",
            new Dictionary<string, object?> { ["Warehouse"] = warehouse, ["DocumentDate"] = new DateTime(2026, 3, 1) },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Items"] = new IDictionary<string, object?>[]
                {
                    new Dictionary<string, object?> { ["Item"] = item, ["Quantity"] = qty, ["Amount"] = qty * 10m },
                },
            },
            "Receipt");
}