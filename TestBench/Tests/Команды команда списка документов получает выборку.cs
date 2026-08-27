// «Ядерные тесты.Команды»: DocumentListCommand привязана к типу TBStockDoc —
// хук получает выборку документов загруженной и считает её.
public partial class TbDocumentListCommandTests
{
    [IntegrationTest("Команды: команда списка документов получает выборку")]
    public async Task DocumentListCommandReceivesSelection()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();
        var first = await CreateDocAsync(warehouse, item, TBStockDoc.Subtypes.Receipt);
        var second = await CreateDocAsync(warehouse, item, TBStockDoc.Subtypes.Issue);

        var commandId = await Db.FindCommandIdAsync("documentlist", "TBStockDocTally");
        var result = await Db.ExecuteDocumentListCommandAsync(commandId, new List<System.Guid> { first, second });
        Assert.IsTrue(result.Success, "команда должна выполниться: {0}", result.Message ?? "");
        Assert.IsTrue(result.ClientMessages.Contains("docs=2"),
            "команда сосчитала оба документа: {0}", string.Join("; ", result.ClientMessages));
        Log("DocumentListCommand получила выборку: docs=2.");
    }

    private Task<System.Guid> CreateDocAsync(System.Guid warehouse, System.Guid item, string subtype)
        => Db.CreateDocumentAsync(
            "TBStockDoc",
            new Dictionary<string, object?> { ["Warehouse"] = warehouse },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Items"] = new IDictionary<string, object?>[]
                {
                    new Dictionary<string, object?> { ["Item"] = item, ["Quantity"] = 1m, ["Amount"] = 100m },
                },
            },
            subtype);
}