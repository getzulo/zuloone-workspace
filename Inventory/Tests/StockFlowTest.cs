using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Integration coverage for the double-entry Stock ledger: an adjustment brings
// stock in from the External bucket, a transfer moves it between two locations
// as one balanced pair, and a write-off beyond on-hand is rejected by the
// StockAdjustment precheck. On-hand is read per (Location,Item) — Stock now has
// physical dimensions, and the register sums to zero (conservation).
public class StockFlowTest : IntegrationTestScriptBase
{
    private async Task<(Guid Loc1, Guid Loc2, Guid Item)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Euro", ["Code"] = "EUR", ["Symbol"] = "€" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Germany", ["CodeISO2"] = "DE", ["CodeISO3"] = "DEU", ["PhoneCode"] = "49" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME GmbH", ["RegistrationNumber"] = "REG-INV-1", ["Country"] = country, ["Currency"] = currency });
        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?>
            { ["Code"] = "WH", ["Name"] = "Warehouse" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?>
            { ["Name"] = "Main", ["LegalEntity"] = le, ["DivisionType"] = dt });

        var wh = await Db.InsertAsync("Store", new Dictionary<string, object?> { ["Name"] = "Central", ["Division"] = div, ["IsSimple"] = true });
        var whZone = await Db.InsertAsync("StoreZone", new Dictionary<string, object?> { ["Name"] = "Зона", ["Store"] = wh, ["IsBarcodeTracking"] = false });
        var lt = await Db.InsertAsync("StoreCellType", new Dictionary<string, object?> {["Code"] = $"STG-{Db.NewId():N}"[..12], ["Name"] = "Storage" });
        var loc1 = await Db.InsertAsync("StoreCell", new Dictionary<string, object?> { ["Name"] = "A-01", ["Type"] = lt, ["StoreZone"] = whZone, ["RackNumber"] = 1, ["ShelfNumber"] = 1, ["LineNumber"] = 1, ["CellNumber"] = 1 });
        var loc2 = await Db.InsertAsync("StoreCell", new Dictionary<string, object?> { ["Name"] = "A-02", ["Type"] = lt, ["StoreZone"] = whZone, ["RackNumber"] = 1, ["ShelfNumber"] = 1, ["LineNumber"] = 1, ["CellNumber"] = 2 });

        var uom = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?>
            { ["Name"] = "Piece", ["Code"] = "PCS" });
        var group = await Db.InsertAsync("ItemGroup", new Dictionary<string, object?>
            { ["Code"] = $"MERCH-{Db.NewId():N}"[..12], ["Name"] = "Merchandise" });
        var item = await Db.InsertAsync("Item", new Dictionary<string, object?>
            { ["Name"] = "Widget", ["ItemGroup"] = group, ["UnitOfMeasure"] = uom });

        return ((Guid)loc1, (Guid)loc2, (Guid)item);
    }

    private async Task PostAdjustmentAsync(Guid location, Guid item, decimal qty)
    {
        var doc = await Db.CreateDocumentAsync("StockAdjustment",
            new Dictionary<string, object?> { ["Cell"] = location },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = item, ["Quantity"] = qty } },
            });
        await Db.ChangeSubtypeAsync("StockAdjustment", doc, "Posted");
    }

    private async Task<decimal> OnHandAsync(Guid location, Guid item)
    {
        decimal q = 0m;
        foreach (var r in await Db.QueryBalancesAsync("Stock", "[Cell] = '" + location + "' AND [Item] = '" + item + "'"))
            q += Convert.ToDecimal(r["Qty"]);
        return q;
    }

    private async Task<decimal> RegisterSumAsync()
    {
        decimal q = 0m;
        foreach (var r in await Db.QueryBalancesAsync("Stock")) q += Convert.ToDecimal(r["Qty"]);
        return q;
    }

    [IntegrationTest("Корректировка вводит остаток из внешнего мира")]
    public async Task AdjustmentAddsStock()
    {
        var s = await SetupAsync();
        await PostAdjustmentAsync(s.Loc1, s.Item, 10m);

        Assert.IsTrue(await OnHandAsync(s.Loc1, s.Item) == 10m, "на ячейке должно быть 10");
        Assert.IsTrue(await RegisterSumAsync() == 0m, "двойная запись: сумма по регистру равна нулю");
    }

    [IntegrationTest("Перемещение делит остаток между ячейками")]
    public async Task TransferSplitsStock()
    {
        var s = await SetupAsync();
        await PostAdjustmentAsync(s.Loc1, s.Item, 10m);

        var doc = await Db.CreateDocumentAsync("StockTransfer",
            new Dictionary<string, object?> { ["FromCell"] = s.Loc1, ["ToCell"] = s.Loc2 },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = s.Item, ["Quantity"] = 4m } },
            });
        await Db.ChangeSubtypeAsync("StockTransfer", doc, "Posted");

        Assert.IsTrue(await OnHandAsync(s.Loc1, s.Item) == 6m, "на исходной ячейке осталось 6");
        Assert.IsTrue(await OnHandAsync(s.Loc2, s.Item) == 4m, "на целевой ячейке 4");

        // Две пары: приход (External −10 / Loc1 +10) и перемещение (Loc1 −4 / Loc2 +4).
        // Считаем ТОЛЬКО движения по своему товару: регистр общий, и незакрытые
        // строки соседних прогонов попадут в безусловный QueryMovementsAsync.
        var moves = await Db.QueryMovementsAsync("Stock", $"[Item] = '{s.Item}'");
        decimal sum = 0m;
        foreach (var m in moves) sum += Convert.ToDecimal(m["Qty"]);
        Assert.IsTrue(moves.Count == 4, "ожидалось 4 движения (две пары), а не {0}", moves.Count);
        Assert.IsTrue(sum == 0m, "двойная запись: сумма движений равна нулю, а не {0}", sum);
    }

    [IntegrationTest("Списание сверх наличия отклоняется")]
    public async Task OverWithdrawIsRejected()
    {
        var s = await SetupAsync();
        await PostAdjustmentAsync(s.Loc1, s.Item, 5m);

        var wo = await Db.CreateDocumentAsync("StockAdjustment",
            new Dictionary<string, object?> { ["Cell"] = s.Loc1 },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = s.Item, ["Quantity"] = -8m } },
            });

        var rejected = false;
        try
        {
            await Db.ChangeSubtypeAsync("StockAdjustment", wo, "Posted");
        }
        catch
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "списание 8 при остатке 5 должно быть отклонено событием");
    }
}
