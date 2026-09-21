using System;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// MonthlySalary > 0 prorates the oklad by hours / weekday-hours of the
// window. Unset monthly keeps hours × HourlyRate. AccruePayroll uses this.
public class PayrollCalculationTest : IntegrationTestScriptBase
{
    private static IPayrollCalculationService Svc => GetService<IPayrollCalculationService>();
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();

    private static readonly DateTime JuneFrom = new(2026, 6, 1);
    private static readonly DateTime JuneTo = new(2026, 6, 30);

    [IntegrationTest("Без оклада начисление остаётся часы × ставка")]
    public async Task HourlyWhenMonthlyIsZero()
    {
        var pos = await NewPositionAsync(50m, 0m);
        var amount = await Svc.AmountOfAsync(pos, 10m, JuneFrom, JuneTo);
        Assert.IsTrue(amount == 500m, "10 × 50 = 500, факт {0}", amount);
    }

    [IntegrationTest("Полный месяц по окладу даёт оклад, половина часов — половину")]
    public async Task MonthlyProratesByStandardHours()
    {
        var pos = await NewPositionAsync(1m, 10000m);
        var standard = await Svc.StandardHoursAsync(JuneFrom, JuneTo);
        Assert.IsTrue(standard > 0m, "в июне есть рабочие дни, факт {0}", standard);

        var full = await Svc.AmountOfAsync(pos, standard, JuneFrom, JuneTo);
        Assert.IsTrue(full == 10000m, "полная норма = оклад 10000, факт {0}", full);

        var halfHours = Math.Round(standard / 2m, 2, MidpointRounding.AwayFromZero);
        var half = await Svc.AmountOfAsync(pos, halfHours, JuneFrom, JuneTo);
        var expectedHalf = Math.Round(10000m * halfHours / standard, 2, MidpointRounding.AwayFromZero);
        Assert.IsTrue(half == expectedHalf,
            "половина нормы, факт {0} ждали {1} (часы {2} / {3})",
            half, expectedHalf, halfHours, standard);
    }

    [IntegrationTest("Отрицательный оклад отклоняется при вводе")]
    public async Task NegativeMonthlySalaryIsRejected()
    {
        var pos = DictionaryManager.NewRecord<Position>();
        pos.Name = "Bad";
        pos.HourlyRate = 10m;
        pos.MonthlySalary = -1m;
        var reason = string.Empty;
        try { await DictionaryManager.SaveRecordAsync(pos); }
        catch (Exception ex) { reason = ex.Message; }
        Assert.IsTrue(reason.Contains("Оклад не может быть отрицательным"),
            "отрицательный оклад отклоняется, факт: {0}", reason);
    }

    private async Task<Guid> NewPositionAsync(decimal hourly, decimal monthly)
    {
        var pos = DictionaryManager.NewRecord<Position>();
        pos.Name = "Role";
        pos.HourlyRate = hourly;
        pos.MonthlySalary = monthly;
        pos = await DictionaryManager.SaveRecordAsync(pos);
        return pos.MetaId;
    }
}
