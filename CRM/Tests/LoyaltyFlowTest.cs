using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Покрытие CRM: выставление Sales-инвойса начисляет баллы лояльности
// (расширение чужой модели через tx-скрипт на подтипе SalesInvoice.Issued),
// списание уменьшает баланс, переспис отклоняется (allowNegativeBalance=false).
public class LoyaltyFlowTest : IntegrationTestScriptBase
{
    private async Task<(Guid Location, Guid Item, Guid Customer)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Euro", ["Code"] = "EUR", ["Symbol"] = "€" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Germany", ["CodeISO2"] = "DE", ["CodeISO3"] = "DEU", ["PhoneCode"] = "49" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME GmbH", ["RegistrationNumber"] = "REG-CRM-1", ["Country"] = country, ["Currency"] = currency });
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

    private async Task<decimal> PointsBalanceAsync()
    {
        // LoyaltyPoints несёт только динамическую аналитику Customer — баланс
        // схлопывается в одну строку, поэтому суммируем все строки остатка.
        decimal sum = 0m;
        foreach (var r in await Db.QueryBalancesAsync("LoyaltyPoints")) sum += Convert.ToDecimal(r["Points"]);
        return sum;
    }

    [IntegrationTest("Выставление счёта начисляет баллы лояльности (расширение Sales)")]
    public async Task IssueEarnsPoints()
    {
        var s = await SetupAsync();
        await Db.PostMovementAsync("Stock", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Location"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        var inv = await Db.CreateDocumentAsync("SalesInvoice",
            new Dictionary<string, object?> { ["Customer"] = s.Customer, ["Location"] = s.Location },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = s.Item, ["Quantity"] = 3m, ["UnitPrice"] = 5m } } });
        await Db.ChangeSubtypeAsync("SalesInvoice", inv, "Issued");

        // 3 × 5 = 15 баллов.
        Assert.IsTrue(await PointsBalanceAsync() == 15m, "начислено 15 баллов, факт {0}", await PointsBalanceAsync());
    }

    [IntegrationTest("Списание уменьшает баланс баллов")]
    public async Task RedeemReducesPoints()
    {
        var customer = Db.NewId();
        await Db.PostMovementAsync("LoyaltyPoints", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Customer"] = customer },
            new Dictionary<string, decimal> { ["Points"] = 15m });

        var doc = await Db.CreateDocumentAsync("LoyaltyRedemption",
            new Dictionary<string, object?> { ["Customer"] = customer, ["Points"] = 10m },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>());
        await Db.ChangeSubtypeAsync("LoyaltyRedemption", doc, "Redeemed");

        Assert.IsTrue(await PointsBalanceAsync() == 5m, "остаток 15 − 10 = 5, факт {0}", await PointsBalanceAsync());
    }

    [IntegrationTest("Списание сверх баланса отклоняется (allowNegativeBalance=false)")]
    public async Task OverRedeemRejected()
    {
        var customer = Db.NewId();
        await Db.PostMovementAsync("LoyaltyPoints", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Customer"] = customer },
            new Dictionary<string, decimal> { ["Points"] = 15m });

        var doc = await Db.CreateDocumentAsync("LoyaltyRedemption",
            new Dictionary<string, object?> { ["Customer"] = customer, ["Points"] = 20m },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>());

        var rejected = false;
        try { await Db.ChangeSubtypeAsync("LoyaltyRedemption", doc, "Redeemed"); }
        catch (Exception) { rejected = true; }
        Assert.IsTrue(rejected, "списание 20 при балансе 15 должно быть отклонено");
    }
}
