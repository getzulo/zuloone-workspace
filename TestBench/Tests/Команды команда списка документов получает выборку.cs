// «Ядерные тесты.Команды»: DocumentListCommand привязана к типу TBStockDoc —
// хук получает выборку документов загруженной и считает её.
//
// Документы выборки собираются типизированно через IDocumentManager. Сам ЗАПУСК
// команды остаётся на харнессе намеренно: диспатч семейств команд живёт в
// CommandFamilyService (тот же путь, что и /api/commands2/...), менеджера над
// ним нет — и именно этот диспатч тест и проверяет.
public partial class TbDocumentListCommandTests
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    [IntegrationTest("Команды: команда списка документов получает выборку")]
    public async Task DocumentListCommandReceivesSelection()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();
        var first = await CreateDocAsync(warehouse, item, TBStockDoc.Subtypes.Receipt);
        var second = await CreateDocAsync(warehouse, item, TBStockDoc.Subtypes.Issue);

        var commandId = await Db.FindCommandIdAsync("documentlist", "TBStockDocTally");
        var result = await Db.ExecuteDocumentListCommandAsync(commandId, new List<System.Guid> { first.MetaId, second.MetaId });
        Assert.IsTrue(result.Success, "команда должна выполниться: {0}", result.Message ?? "");
        Assert.IsTrue(result.ClientMessages.Contains("docs=2"),
            "команда сосчитала оба документа: {0}", string.Join("; ", result.ClientMessages));
        Log("DocumentListCommand получила выборку: docs=2.");
    }

    private async Task<TBStockDoc> CreateDocAsync(System.Guid warehouse, System.Guid item, string subtype)
    {
        var doc = await DocumentManager.NewDocumentAsync<TBStockDoc>(subtype);
        doc.Warehouse = warehouse;
        doc.Items.Add(new TBLinesTablePartRow { Item = item, Quantity = 1m, Amount = 100m });
        await DocumentManager.SaveDocumentAsync(doc);
        return doc;
    }
}
