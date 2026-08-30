// «Ядерные тесты.Команды»: DocumentCommand привязана ТОЛЬКО к подтипу Receipt.
// Против Receipt — исполняется и читает документ; против Issue — отклоняется
// MIQS-проверкой CheckDocumentSubtype (Success=false, без исключения).
public partial class TbDocumentCommandTests
{
    [IntegrationTest("Команды: команда документа уважает привязку подтипов")]
    public async Task DocumentCommandHonoursSubtypeBinding()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();
        var commandId = await Db.FindCommandIdAsync("document", "TBStockDocInspect");

        // Привязанный подтип: команда проходит и видит документ.
        var receipt = await CreateDocAsync(warehouse, item, TBStockDoc.Subtypes.Receipt);
        var allowed = await Db.ExecuteDocumentCommandAsync(commandId, receipt);
        Assert.IsTrue(allowed.Success, "против Receipt команда должна выполниться: {0}", allowed.Message ?? "");
        Assert.IsTrue(allowed.ClientMessages.Contains("subtype=Receipt"),
            "хук прочитал документ (его подтип): {0}", string.Join("; ", allowed.ClientMessages));

        // НЕпривязанный подтип: MIQS CheckDocumentSubtype отклоняет исполнение.
        var issue = await CreateDocAsync(warehouse, item, TBStockDoc.Subtypes.Issue);
        var rejected = await Db.ExecuteDocumentCommandAsync(commandId, issue);
        Assert.IsFalse(rejected.Success, "против Issue команда обязана быть отклонена");
        Assert.IsTrue((rejected.Message ?? "").Contains("not bound"),
            "причина отказа — субтиповая привязка: {0}", rejected.Message ?? "");
        Assert.AreEqual(0, rejected.ClientActionCount, "отклонённая команда не исполняла скрипт");
        Log("Гард подтипов подтверждён: Receipt прошёл, Issue отклонён (" + (rejected.Message ?? "") + ").");
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