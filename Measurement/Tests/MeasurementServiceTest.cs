using System;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Покрытие сервиса измерений: округление по настроенной точности
// (QuantityScale=3, AmountScale=2) и по произвольному числу знаков.
public class MeasurementServiceTest : IntegrationTestScriptBase
{
    [IntegrationTest("Сервис округляет по настроенной точности")]
    public async Task RoundsByConfiguredScale()
    {
        await Task.CompletedTask;
        var m = GetService<IMeasurementService>();

        Assert.IsTrue(m.RoundQuantity(1.23456m) == 1.235m, "количество 3 знака = 1.235, факт {0}", m.RoundQuantity(1.23456m));
        Assert.IsTrue(m.RoundAmount(1.235m) == 1.24m, "сумма 2 знака = 1.24, факт {0}", m.RoundAmount(1.235m));
        Assert.IsTrue(m.Round(1.5m, 0) == 2m, "до целого = 2, факт {0}", m.Round(1.5m, 0));
        Assert.IsTrue(m.Round(2.34567m, 4) == 2.3457m, "4 знака = 2.3457, факт {0}", m.Round(2.34567m, 4));
    }
}
