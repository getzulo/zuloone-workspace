using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Покрытие PricingService: сумма строки = количество × цена, округлённая по
// денежной точности. Заодно проверяет КОМПОЗИЦИЮ сервисов — PricingService
// внутри зовёт IMeasurementService (инъекция через конструктор).
public class PricingServiceTest : IntegrationTestScriptBase
{
    [IntegrationTest("Сумма строки = количество × цена, округлённая до денежной точности")]
    public async Task LineAmountRoundsProduct()
    {
        await Task.CompletedTask;
        var pricing = GetService<IPricingService>();

        Assert.IsTrue(pricing.LineAmount(3m, 5m) == 15m, "3 × 5 = 15, факт {0}", pricing.LineAmount(3m, 5m));
        Assert.IsTrue(pricing.LineAmount(2m, 3.333m) == 6.67m, "2 × 3.333 = 6.666 → 6.67, факт {0}", pricing.LineAmount(2m, 3.333m));
        Assert.IsTrue(pricing.LineAmount(0m, 99m) == 0m, "0 × 99 = 0, факт {0}", pricing.LineAmount(0m, 99m));
    }
}
