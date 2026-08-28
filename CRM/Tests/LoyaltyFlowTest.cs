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

    // Customer — ФИЗИЧЕСКОЕ измерение регистра, поэтому баланс спрашивается по
    // конкретному клиенту: баллы — это его лицевой счёт, а не общий котёл.
    private async Task<decimal> PointsBalanceAsync(Guid customer)
    {
        decimal sum = 0m;
        foreach (var r in await Db.QueryBalancesAsync("LoyaltyPoints", $"[Customer] = '{customer}'"))
            sum += Convert.ToDecimal(r["Points"]);
        return sum;
    }

    private Task<Guid> TierAsync(string name, decimal minPoints, decimal maxPerDoc)
        => Db.InsertAsync("LoyaltyTier", new Dictionary<string, object?>
            { ["Name"] = name, ["MinPoints"] = minPoints, ["MaxRedemptionPerDocument"] = maxPerDoc,
              ["DiscountPercent"] = 0m });

    [IntegrationTest("Выставление счёта начисляет баллы лояльности (расширение Sales)")]
    public async Task IssueEarnsPoints()
    {
        var s = await SetupAsync();
        await Db.PostMovementAsync("Stock", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Cell"] = s.Location, ["Item"] = s.Item },
            new Dictionary<string, decimal> { ["Qty"] = 10m });

        var inv = await Db.CreateDocumentAsync("SalesInvoice",
            new Dictionary<string, object?> { ["Customer"] = s.Customer, ["Location"] = s.Location },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            { ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = s.Item, ["Quantity"] = 3m, ["UnitPrice"] = 5m } } });
        await Db.ChangeSubtypeAsync("SalesInvoice", inv, "Issued");

        // 3 × 5 = 15 баллов.
        Assert.IsTrue(await PointsBalanceAsync(s.Customer) == 15m, "начислено 15 баллов, факт {0}", await PointsBalanceAsync(s.Customer));
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

        Assert.IsTrue(await PointsBalanceAsync(customer) == 5m, "остаток 15 − 10 = 5, факт {0}", await PointsBalanceAsync(customer));
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

    [IntegrationTest("Уровень ограничивает списание за один документ")]
    public async Task TierCapsRedemption()
    {
        var customer = Db.NewId();
        await Db.PostMovementAsync("LoyaltyPoints", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Customer"] = customer },
            new Dictionary<string, decimal> { ["Points"] = 500m });

        // Клиент с балансом 500 попадает на Silver (порог 100), а не на Gold (1000):
        // берётся САМЫЙ ВЫСОКИЙ достигнутый уровень, значит лимит 50, не 500.
        await TierAsync("Bronze", 0m, 10m);
        await TierAsync("Silver", 100m, 50m);
        await TierAsync("Gold", 1000m, 500m);

        var doc = await Db.CreateDocumentAsync("LoyaltyRedemption",
            new Dictionary<string, object?> { ["Customer"] = customer, ["Points"] = 200m },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>());

        var reason = "";
        try { await Db.ChangeSubtypeAsync("LoyaltyRedemption", doc, "Redeemed"); }
        catch (Exception ex) { reason = ex.Message; }
        Assert.IsTrue(reason.Length > 0, "200 баллов сверх лимита уровня Silver (50) — должно быть отклонено");
        // Причина важна: без этой проверки тест зеленел бы от любой поломки внутри события.
        Assert.IsTrue(reason.Contains("Silver"), "отказ должен ссылаться на лимит уровня Silver, а не на другую ошибку: {0}", reason);
        Assert.IsTrue(await PointsBalanceAsync(customer) == 500m,
            "отклонённое погашение не трогает баланс, факт {0}", await PointsBalanceAsync(customer));
    }

    [IntegrationTest("Списание в пределах лимита уровня проходит")]
    public async Task WithinTierCapRedeems()
    {
        var customer = Db.NewId();
        await Db.PostMovementAsync("LoyaltyPoints", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Customer"] = customer },
            new Dictionary<string, decimal> { ["Points"] = 500m });

        await TierAsync("Bronze", 0m, 10m);
        await TierAsync("Silver", 100m, 50m);

        var doc = await Db.CreateDocumentAsync("LoyaltyRedemption",
            new Dictionary<string, object?> { ["Customer"] = customer, ["Points"] = 50m },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>());
        await Db.ChangeSubtypeAsync("LoyaltyRedemption", doc, "Redeemed");

        Assert.IsTrue(await PointsBalanceAsync(customer) == 450m,
            "ровно лимит уровня списывается: 500 − 50 = 450, факт {0}", await PointsBalanceAsync(customer));
    }
}
