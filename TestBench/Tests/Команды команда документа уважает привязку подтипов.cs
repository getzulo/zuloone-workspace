// «Ядерные тесты.Команды»: DocumentCommand привязана ТОЛЬКО к подтипу Receipt.
// Против Receipt — исполняется и читает документ; против Issue — отклоняется
// MIQS-проверкой CheckDocumentSubtype (Success=false, без исключения).
//
// Документы строятся типизированным IDocumentManager (NewDocumentAsync → строки
// табличной части → SaveDocumentAsync). Исполнение команды остаётся на Db:
// семейства команд MIQS не имеют менеджера в платформе.
public partial class TbDocumentCommandTests
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    [IntegrationTest("Команды: команда документа уважает привязку подтипов")]
    public async Task DocumentCommandHonoursSubtypeBinding()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();
        var commandId = await Db.FindCommandIdAsync("document", "TBStockDocInspect");

        // Привязанный подтип: команда проходит и видит документ.
        var receipt = await CreateDocAsync(warehouse, item, TBStockDoc.Subtypes.Receipt);
        var allowed = await Db.ExecuteDocumentCommandAsync(commandId, receipt.MetaId);
        Assert.IsTrue(allowed.Success, "против Receipt команда должна выполниться: {0}", allowed.Message ?? "");
        Assert.IsTrue(allowed.ClientMessages.Contains("subtype=Receipt"),
            "хук прочитал документ (его подтип): {0}", string.Join("; ", allowed.ClientMessages));

        // НЕпривязанный подтип: MIQS CheckDocumentSubtype отклоняет исполнение.
        var issue = await CreateDocAsync(warehouse, item, TBStockDoc.Subtypes.Issue);
        var rejected = await Db.ExecuteDocumentCommandAsync(commandId, issue.MetaId);
        Assert.IsFalse(rejected.Success, "против Issue команда обязана быть отклонена");
        Assert.IsTrue((rejected.Message ?? "").Contains("not bound"),
            "причина отказа — субтиповая привязка: {0}", rejected.Message ?? "");
        Assert.AreEqual(0, rejected.ClientActionCount, "отклонённая команда не исполняла скрипт");
        Log("Гард подтипов подтверждён: Receipt прошёл, Issue отклонён (" + (rejected.Message ?? "") + ").");
    }

    private static async Task<TBStockDoc> CreateDocAsync(Guid warehouse, Guid item, string subtype)
    {
        var doc = await DocumentManager.NewDocumentAsync<TBStockDoc>(subtype);
        doc.Warehouse = warehouse;
        doc.Items.Add(new TBLinesTablePartRow { Item = item, Quantity = 1m, Amount = 100m });
        await DocumentManager.SaveDocumentAsync(doc);
        return doc;
    }
}
