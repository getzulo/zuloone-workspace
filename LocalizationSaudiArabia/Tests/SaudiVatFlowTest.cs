using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Покрытие локализации КСА: выставление Sales-инвойса начисляет НДС 15% в
// регистр VatPayable (ставка берётся из глобальной константы SaudiVatRate).
public class SaudiVatFlowTest : IntegrationTestScriptBase
{
    private async Task<(Guid Location, Guid Item, Guid Customer)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Riyal", ["Code"] = "SAR", ["Symbol"] = "﷼" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Saudi Arabia", ["CodeISO2"] = "SA", ["CodeISO3"] = "SAU", ["PhoneCode"] = "966" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "Riyadh Trading", ["RegistrationNumber"] = "REG-SA-1", ["Country"] = country, ["Currency"] = currency });
        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?> { ["Code"] = "SP", ["Name"] = "SalesPoint" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?> { ["Name"] = "Shop", ["LegalEntity"] = le, ["DivisionType"] = dt });
        var wh = await Db.InsertAsync("Warehouse", new Dictionary<string, object?> { ["Name"] = "Shop WH", ["Division"] = div });
        var lt = await Db.InsertAsync("LocationType", new Dictionary<string, object?> { ["Code"] = "PICK", ["Name"] = "Picking" });
        var loc = await Db.InsertAsync("WarehouseLocation", new Dictionary<string, object?> { ["Warehouse"] = wh, ["Name"] = "P-01", ["LocationType"] = lt });

        var uom = await Db.InsertAsync("UnitOfMeasure", new Dictionary<string, object?> { ["Name"] = "Piece", ["Code"] = "PCS" });
        var group = await Db.InsertAsync("ItemGroup", new Dictionary<string, object?> { ["Code"] = "GOODS", ["Name"] = "Finished goods" });
        var item = await Db.InsertAsync("Item", new Dictionary<string, object?>
            { ["Name"] = "Gadget", ["ItemGroup"] = group, ["UnitOfMeasure"] = uom, ["IsSellable"] = true });
        var customer = await Db.InsertAsync("Customer", new Dictionary<string, object?>
            { ["Name"] = "Buyer Ltd", ["CustomerType"] = "B2B" });

        return ((Guid)loc, (Guid)item, (Guid)customer);
    }

    [IntegrationTest("Выставление счёта начисляет НДС 15% в VatPayable")]
    public async Task IssueAccruesVat()
    {
        var s = await SetupAsync();
        await Db.PostMovementAsync("Stock", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Location"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 20m });

        var inv = await Db.CreateDocumentAsync("SalesInvoice",
            new Dictionary<string, object?> { ["Customer"] = s.Customer, ["Location"] = s.Location },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = s.Item, ["Quantity"] = 10m, ["UnitPrice"] = 10m } } });
        await Db.ChangeSubtypeAsync("SalesInvoice", inv, "Issued");

        // База 10 × 10 = 100; НДС 15% = 15. VatPayable несёт одну динамическую
        // аналитику (Customer) — баланс схлопывается в одну строку, суммируем.
        decimal vat = 0m;
        foreach (var r in await Db.QueryBalancesAsync("VatPayable")) vat += Convert.ToDecimal(r["Amount"]);
        Assert.IsTrue(vat == 15m, "НДС 15 при базе 100, факт {0}", vat);
    }
}
