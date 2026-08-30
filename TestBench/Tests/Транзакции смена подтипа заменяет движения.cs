// Смена подтипа через МЕНЕДЖЕР ДОКУМЕНТОВ (не через базу и не по имени типа):
// подтип — это ПРИСВОЕНИЕ плюс сохранение (MIQS doc.SubtypeID = …; SaveDocument),
// а SaveDocumentAsync проводит смену через движок с replace-семантикой.
// Со статусом ровно то же самое: doc.StatusValue = …; SaveDocumentAsync.
// Подтипы — ГЕНЕРЁННЫЕ константы TBStockDoc.Subtypes.
public partial class TbSubtypeSwitchTests
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("Транзакции: смена подтипа заменяет движения")]
    public async Task SubtypeSwitchReplacesMovements()
    {
        var warehouse = Db.NewId(); // Db.NewId() — законный остаток: генерация id, не доступ к данным.
        var item = Db.NewId();

        var doc = await DocumentManager.NewDocumentAsync<TBStockDoc>(TBStockDoc.Subtypes.Receipt);
        doc.Warehouse = warehouse;
        doc.Items.Add(new TBLinesTablePartRow { Item = item, Quantity = 7m, Amount = 700m });
        await DocumentManager.SaveDocumentAsync(doc);

        var key = new Dictionary<string, object?> { ["Warehouse"] = warehouse, ["Item"] = item };
        // Состояние ДО проведения: черновик регистр не двигает, иначе «приход даёт 7»
        // ничего не доказывает про переход.
        Assert.AreEqual(0m, await TotalsManager.GetBalanceAsync("TBStock", "Quantity", key),
            "черновик регистр не двигает");

        // Проведение — та же идиома, что и смена подтипа ниже: присвоение плюс
        // сохранение. Изменившийся StatusValue SaveDocumentAsync проводит через
        // движок, колонкой он не пишется.
        doc.StatusValue = "Posted";
        await DocumentManager.SaveDocumentAsync(doc);
        Assert.AreEqual(7m, await TotalsManager.GetBalanceAsync("TBStock", "Quantity", key), "приход Receipt даёт 7");

        // Функциональный путь: движок снимает движения Receipt и исполняет цепочку
        // ReceiptChain — и запускается он присваиванием подтипа, а не вызовом по имени.
        doc = (await DocumentManager.GetDocumentAsync<TBStockDoc>(doc.MetaId))!;
        doc.Subtype = TBStockDoc.Subtypes.ReceiptChain;
        await DocumentManager.SaveDocumentAsync(doc);
        Assert.AreEqual(107m, await TotalsManager.GetBalanceAsync("TBStock", "Quantity", key), "цепочка: 7 + плоские 100");

        var moves = await TotalsManager.QueryMovementsAsync("TBStock", "[DocumentMetaId] = '" + doc.MetaId + "'");
        Assert.AreEqual(2, moves.Count, "две TR-строки цепочки");
        var provenance = new HashSet<string>();
        foreach (var m in moves) provenance.Add(System.Convert.ToString(m["ScriptMetaId"]) ?? "");
        Assert.AreEqual(2, provenance.Count, "у каждой строки цепочки свой скрипт");

        // Обратно на Receipt: движения снова заменены одиночным приходом.
        doc = (await DocumentManager.GetDocumentAsync<TBStockDoc>(doc.MetaId))!;
        doc.Subtype = TBStockDoc.Subtypes.Receipt;
        await DocumentManager.SaveDocumentAsync(doc);
        Assert.AreEqual(7m, await TotalsManager.GetBalanceAsync("TBStock", "Quantity", key), "возврат подтипа вернул 7");

        // Ни смена статуса, ни две смены подтипа не должны были потерять то, что
        // платформа выдала документу при вставке.
        var stored = (await DocumentManager.GetDocumentAsync<TBStockDoc>(doc.MetaId))!;
        Assert.AreEqual("Posted", stored.StatusValue, "документ остался проведённым");
        Assert.IsFalse(string.IsNullOrWhiteSpace(stored.Number), "номер документа пережил смену статуса и подтипа");

        Log("Replace-семантика подтипов подтверждена менеджером документов.");
    }
}
