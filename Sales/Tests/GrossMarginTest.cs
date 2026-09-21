using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Margin of a sale is Revenue minus the InventoryValue write-off posted
// on the same recorder. A write-off without Revenue (scrap) must not
// eat the number — GL already keeps those on a different account.
public class GrossMarginTest : IntegrationTestScriptBase
{
    private static readonly Guid RevenueRegister = Guid.Parse("103cdb23-2d49-4c42-9bf3-3c5a8ce46cdc");
    private static readonly Guid InventoryValueRegister = Guid.Parse("7e0b4d85-1a6f-4c9d-8e5b-8f1a4c7d0e60");
    private static readonly DateTime Day = new(2026, 6, 15);
    private static readonly DateTime From = new(2026, 6, 1);
    private static readonly DateTime To = new(2026, 7, 1);

    private static IGrossMarginService Svc => GetService<IGrossMarginService>();
    private static IRegisterMovementService Movements => GetService<IRegisterMovementService>();

    [IntegrationTest("Маржа продажи = выручка минус списание на том же документе")]
    public async Task MarginIsRevenueMinusSaleCogs()
    {
        var item = Db.NewId();
        var sale = Db.NewId();
        await PostRevenueAsync(sale, item, 1000m);
        await PostValueAsync(sale, item, -1m, -400m);

        var revenue = await Svc.RevenueOfAsync(item, From, To);
        var cogs = await Svc.CogsOfAsync(item, From, To);
        var margin = await Svc.MarginOfAsync(item, From, To);
        Assert.IsTrue(revenue == 1000m, "выручка 1000, факт {0}", revenue);
        Assert.IsTrue(cogs == 400m, "себестоимость продажи 400, факт {0}", cogs);
        Assert.IsTrue(margin == 600m, "маржа 600, факт {0}", margin);
    }

    [IntegrationTest("Списание без выручки не входит в себестоимость продажи")]
    public async Task ScrapWithoutRevenueStaysOut()
    {
        var item = Db.NewId();
        var sale = Db.NewId();
        var scrap = Db.NewId();
        await PostRevenueAsync(sale, item, 500m);
        await PostValueAsync(sale, item, -1m, -200m);
        await PostValueAsync(scrap, item, -1m, -50m);

        var cogs = await Svc.CogsOfAsync(item, From, To);
        var margin = await Svc.MarginOfAsync(item, From, To);
        Assert.IsTrue(cogs == 200m, "бой 50 вне маржи, факт {0}", cogs);
        Assert.IsTrue(margin == 300m, "500 − 200, факт {0}", margin);
    }

    [IntegrationTest("Чужой товар и чужой период не смешиваются")]
    public async Task OtherItemAndPeriodStayOut()
    {
        var item = Db.NewId();
        var other = Db.NewId();
        var sale = Db.NewId();
        await PostRevenueAsync(sale, item, 100m);
        await PostValueAsync(sale, item, -1m, -40m);
        await PostRevenueAsync(sale, other, 999m);
        await PostValueAsync(sale, other, -1m, -1m);

        Assert.IsTrue(await Svc.MarginOfAsync(item, From, To) == 60m, "свой товар");
        Assert.IsTrue(await Svc.MarginOfAsync(item, new DateTime(2026, 1, 1), new DateTime(2026, 2, 1)) == 0m,
            "чужой период — ноль");
    }

    private async Task PostRevenueAsync(Guid document, Guid item, decimal amount)
        => await Movements.PostMovementAsync(
            RevenueRegister,
            document,
            Day,
            new Dictionary<string, object?>(),
            new Dictionary<string, decimal> { ["Amount"] = amount },
            analytics: new Dictionary<string, object?>
            {
                ["Item"] = item,
                ["Customer"] = Db.NewId(),
                ["SalesContract"] = Db.NewId()
            });

    private async Task PostValueAsync(Guid document, Guid item, decimal qty, decimal value)
        => await Movements.PostMovementAsync(
            InventoryValueRegister,
            document,
            Day,
            new Dictionary<string, object?>(),
            new Dictionary<string, decimal> { ["Qty"] = qty, ["Value"] = value },
            analytics: new Dictionary<string, object?> { ["Item"] = item });
}
