using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Контур дебиторки: выставленный счёт создаёт долг покупателя, а отдельный
// документ «Оплата покупателя» его гасит.
//
// Почему оплата — документ, а не подтип счёта: смена подтипа снимает движения
// ПРОШЛОГО состояния, поэтому вариант «Выставлен → Оплачен» обнулял вместе с
// долгом и ВЫРУЧКУ, то есть отменял продажу. Тест это и поймал.
public class ReceivableFlowTest : IntegrationTestScriptBase
{
    private async Task<(Guid Location, Guid Item, Guid Customer)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Euro", ["Code"] = "EUR", ["Symbol"] = "€" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Germany", ["CodeISO2"] = "DE", ["CodeISO3"] = "DEU", ["PhoneCode"] = "49" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME GmbH", ["RegistrationNumber"] = "REG-AR-1", ["Country"] = country, ["Currency"] = currency });
        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?> { ["Code"] = "SP", ["Name"] = "SalesPoint" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?> { ["Name"] = "Shop", ["LegalEntity"] = le, ["DivisionType"] = dt });
        var wh = await Db.InsertAsync("Store", new Dictionary<string, object?> { ["Name"] = "Shop WH", ["Division"] = div, ["IsSimple"] = true });
        var whZone = await Db.InsertAsync("StoreZone", new Dictionary<string, object?> { ["Name"] = "Зона", ["Store"] = wh, ["IsBarcodeTracking"] = false });
        var lt = await Db.InsertAsync("StoreCellType", new Dictionary<string, object?> {["Code"] = $"PICK-{Db.NewId():N}"[..12], ["Name"] = "Picking" });
        var loc = await Db.InsertAsync("StoreCell", new Dictionary<string, object?> { ["Name"] = "P-01", ["Type"] = lt, ["StoreZone"] = whZone, ["RackNumber"] = 1, ["ShelfNumber"] = 1, ["LineNumber"] = 1, ["CellNumber"] = 1 });

        var uom = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?> { ["Name"] = "Piece", ["Code"] = "PCS" });
        var group = await Db.InsertAsync("ItemGroup", new Dictionary<string, object?> { ["Code"] = "GOODS", ["Name"] = "Finished goods" });
        var item = await Db.InsertAsync("Item", new Dictionary<string, object?>
            { ["Name"] = "Gadget", ["ItemGroup"] = group, ["UnitOfMeasure"] = uom, ["IsSellable"] = true });
        var customer = await Db.InsertAsync("Customer", new Dictionary<string, object?>
            { ["Name"] = "Buyer Ltd", ["CustomerType"] = "B2B" });

        return ((Guid)loc, (Guid)item, (Guid)customer);
    }

    private async Task<decimal> SumAsync(string register)
    {
        decimal total = 0m;
        foreach (var r in await Db.QueryBalancesAsync(register))
            total += Convert.ToDecimal(r[register == "Receivable" ? "Amount" : "Amount"]);
        return total;
    }

    [IntegrationTest("Выставление создаёт дебиторку, оплата её гасит")]
    public async Task IssueCreatesDebtPaymentClearsIt()
    {
        var s = await SetupAsync();

        // Товар на складе, чтобы выставление прошло проверку остатка.
        await Db.PostMovementAsync("Stock", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        var inv = await Db.CreateDocumentAsync("SalesInvoice",
            new Dictionary<string, object?> { ["Customer"] = s.Customer, ["Location"] = s.Location },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = s.Item, ["Quantity"] = 3m, ["UnitPrice"] = 5m } } },
            subtype: "Draft");
        await Db.ChangeSubtypeAsync("SalesInvoice", (Guid)inv, "Issued");

        Assert.IsTrue(await SumAsync("Receivable") == 15m,
            "после выставления долг 3×5=15, факт {0}", await SumAsync("Receivable"));
        Assert.IsTrue(await SumAsync("Revenue") == 15m,
            "выручка признана 15, факт {0}", await SumAsync("Revenue"));

        // Оплата — ОТДЕЛЬНЫЙ документ, а не смена подтипа счёта.
        var pay = await Db.CreateDocumentAsync("CustomerPayment",
            new Dictionary<string, object?>(),
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Customer"] = s.Customer, ["Amount"] = 15m } } },
            subtype: "Draft");
        await Db.ChangeSubtypeAsync("CustomerPayment", (Guid)pay, "Paid");

        Assert.IsTrue(await SumAsync("Receivable") == 0m,
            "после оплаты долг погашен, факт {0}", await SumAsync("Receivable"));

        // Счёт остаётся выставленным — оплата не отменяет продажу.
        var doc = await Db.GetAsync("SalesInvoice", (Guid)inv);
        Assert.IsTrue(Convert.ToString(doc!["Subtype"]) == "Issued",
            "счёт остаётся Issued, факт {0}", Convert.ToString(doc["Subtype"]));

        Assert.IsTrue(await SumAsync("Revenue") == 15m,
            "выручка сохраняется после оплаты, факт {0}", await SumAsync("Revenue"));

        decimal onHand = 0m;
        foreach (var r in await Db.QueryBalancesAsync("Stock", $"[Cell] = '{s.Location}'"))
            onHand += Convert.ToDecimal(r["Qty"]);
        Assert.IsTrue(onHand == 7m, "на ячейке осталось 10−3=7, факт {0}", onHand);
    }
}
