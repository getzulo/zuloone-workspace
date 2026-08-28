using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Покрытие GL-интеграции: выставление Sales-инвойса при настроенных счетах
// разноски создаёт сбалансированную проводку в главной книге
// (Dr дебиторка = Cr выручка = сумма счёта).
public class SalesGLPostingTest : IntegrationTestScriptBase
{
    [IntegrationTest("Выставление счёта разносится в GL: Dr дебиторка = Cr выручка")]
    public async Task IssuePostsBalancedGL()
    {
        var today = DateTime.UtcNow.Date;

        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Euro", ["Code"] = "EUR", ["Symbol"] = "€" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Germany", ["CodeISO2"] = "DE", ["CodeISO3"] = "DEU", ["PhoneCode"] = "49" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME GmbH", ["RegistrationNumber"] = "REG-GL-1", ["Country"] = country, ["Currency"] = currency });
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

        // Настроенные счета разноски (коды совпадают с профилем AccountingSettings).
        await Db.InsertAsync("ChartOfAccounts", new Dictionary<string, object?>
            { ["Code"] = "1200", ["Name"] = "Accounts receivable", ["AccountType"] = "Asset", ["IsPostable"] = true, ["Currency"] = currency });
        await Db.InsertAsync("ChartOfAccounts", new Dictionary<string, object?>
            { ["Code"] = "4000", ["Name"] = "Sales revenue", ["AccountType"] = "Income", ["IsPostable"] = true, ["Currency"] = currency });

        // Профиль разноски — одиночный справочник настроек учёта: именно отсюда
        // GeneralLedgerService берёт коды счетов (раньше были глобальные константы).
        await Db.InsertAsync("AccountingSettings", new Dictionary<string, object?>
        {
            ["ArAccountCode"] = "1200", ["RevenueAccountCode"] = "4000",
            ["InventoryAccountCode"] = "1400", ["PayableAccountCode"] = "2000",
        });

        // Учётный год и период, покрывающий сегодня.
        var fy = await Db.InsertAsync("FiscalYear", new Dictionary<string, object?>
            { ["Code"] = "FY", ["StartDate"] = today.AddMonths(-6), ["EndDate"] = today.AddMonths(6), ["IsClosed"] = false });
        await Db.InsertAsync("FiscalPeriod", new Dictionary<string, object?>
            { ["Code"] = "P1", ["FiscalYear"] = fy, ["FromDate"] = today.AddDays(-15), ["ToDate"] = today.AddDays(15), ["Status"] = "Open" });

        // Товар на складе, затем выставляем счёт на 3 × 5 = 15.
        await Db.PostMovementAsync("Stock", today,
            new Dictionary<string, object?> { ["Cell"] = loc, ["Item"] = item },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        var inv = await Db.CreateDocumentAsync("SalesInvoice",
            new Dictionary<string, object?> { ["Customer"] = customer, ["Location"] = loc },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = item, ["Quantity"] = 3m, ["UnitPrice"] = 5m } } });
        await Db.ChangeSubtypeAsync("SalesInvoice", inv, "Issued");

        // GL несёт динамические аналитики (Account/LegalEntity/FiscalPeriod) — баланс
        // схлопывается, суммируем дебет и кредит по всем строкам.
        decimal debit = 0m, credit = 0m;
        foreach (var r in await Db.QueryBalancesAsync("GL"))
        {
            debit += Convert.ToDecimal(r["Debit"]);
            credit += Convert.ToDecimal(r["Credit"]);
        }
        Assert.IsTrue(debit == 15m, "дебет GL = 15 (дебиторка), факт {0}", debit);
        Assert.IsTrue(credit == 15m, "кредит GL = 15 (выручка), факт {0}", credit);
        Assert.IsTrue(debit == credit, "проводка сбалансирована: дебет {0} = кредит {1}", debit, credit);
    }
}
