// Смена подтипа через ДВИЖОК (не через базу): движения заменяются по MIQS
// replace-семантике; подтипы — ГЕНЕРЁННЫЕ константы TBStockDoc.Subtypes.
public partial class TbSubtypeSwitchTests
{
    [IntegrationTest("Транзакции: смена подтипа заменяет движения")]
    public async Task SubtypeSwitchReplacesMovements()
    {
        var warehouse = Db.NewId();
        var item = Db.NewId();
        var doc = await Db.CreateDocumentAsync(
            "TBStockDoc",
            new Dictionary<string, object?> { ["Warehouse"] = warehouse },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Items"] = new IDictionary<string, object?>[]
                {
                    new Dictionary<string, object?> { ["Item"] = item, ["Quantity"] = 7m, ["Amount"] = 700m },
                },
            },
            TBStockDoc.Subtypes.Receipt);
        await Db.ChangeStatusAsync("TBStockDoc", doc, "Posted");

        var filter = "[Warehouse] = '" + warehouse + "' AND [Item] = '" + item + "'";
        var bal = await Db.QueryBalancesAsync("TBStock", filter);
        Assert.AreEqual(7m, System.Convert.ToDecimal(bal[0]["Quantity"]), "приход Receipt даёт 7");

        // Функциональный путь: движок снимает движения Receipt и исполняет цепочку ReceiptChain.
        await Db.ChangeSubtypeAsync("TBStockDoc", doc, TBStockDoc.Subtypes.ReceiptChain);
        bal = await Db.QueryBalancesAsync("TBStock", filter);
        Assert.AreEqual(107m, System.Convert.ToDecimal(bal[0]["Quantity"]), "цепочка: 7 + плоские 100");
        var moves = await Db.QueryMovementsAsync("TBStock", "[DocumentMetaId] = '" + doc + "'");
        Assert.AreEqual(2, moves.Count, "две TR-строки цепочки");
        var provenance = new HashSet<string>();
        foreach (var m in moves) provenance.Add(System.Convert.ToString(m["ScriptMetaId"]) ?? "");
        Assert.AreEqual(2, provenance.Count, "у каждой строки цепочки свой скрипт");

        // Обратно на Receipt: движения снова заменены одиночным приходом.
        await Db.ChangeSubtypeAsync("TBStockDoc", doc, TBStockDoc.Subtypes.Receipt);
        bal = await Db.QueryBalancesAsync("TBStock", filter);
        Assert.AreEqual(7m, System.Convert.ToDecimal(bal[0]["Quantity"]), "возврат подтипа вернул 7");
        Log("Replace-семантика подтипов подтверждена движком.");
    }
}