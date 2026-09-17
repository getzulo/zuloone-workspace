using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

public class BuySellProfileTest : IntegrationTestScriptBase
{
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITradeProfileService Profile => GetService<ITradeProfileService>();

    [IntegrationTest("BuySell отказывает сохранение производственного заказа")]
    public async Task BuySellRefusesProductionOrder()
    {
        await Profile.SetAsync("BuySell");
        Assert.IsTrue(await Profile.IsBuySellAsync(), "профиль BuySell");

        var order = await DocumentManager.NewDocumentAsync<ProductionOrder>();
        order.Product = Db.NewId();
        order.Location = Db.NewId();
        order.Quantity = 1m;
        var reason = string.Empty;
        try
        {
            await DocumentManager.SaveDocumentAsync(order);
        }
        catch (Exception ex) { reason = ex.Message; }
        Assert.IsTrue(reason.Contains("купил-продал", StringComparison.OrdinalIgnoreCase),
            "отказ про профиль: {0}", reason);
    }

    [IntegrationTest("Full снова разрешает производство")]
    public async Task FullAllowsProductionOrder()
    {
        await Profile.SetAsync("Full");
        Assert.IsTrue(await Profile.CurrentNameAsync() == "Full", "профиль Full");
        Assert.IsTrue(await Profile.ProductionBlockReasonAsync() == null,
            "Full не блокирует выпуск");
    }

    [IntegrationTest("EnsureBuySell не затирает явный Full")]
    public async Task EnsureBuySellLeavesFullAlone()
    {
        await Profile.SetAsync("Full");
        await Profile.EnsureBuySellAsync();
        Assert.IsTrue(await Profile.CurrentNameAsync() == "Full",
            "явный Full остаётся, факт {0}", await Profile.CurrentNameAsync());
    }
}
