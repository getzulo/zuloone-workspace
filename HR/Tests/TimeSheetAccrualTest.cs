using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Табель → начисление: команда читает часы, берёт ставку из должности сотрудника
// и порождает проведённое начисление ФОТ. Проверяется именно расчёт по ставке —
// суммы в табеле нет, она появляется только из связки часы × ставка.
public class TimeSheetAccrualTest : IntegrationTestScriptBase
{
    private async Task<(Guid Division, Guid Emp1, Guid Emp2)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Euro", ["Code"] = "EUR", ["Symbol"] = "€" });
        var country = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Germany", ["CodeISO2"] = "DE", ["CodeISO3"] = "DEU", ["PhoneCode"] = "49" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME GmbH", ["RegistrationNumber"] = "REG-TS-1", ["Country"] = country, ["Currency"] = currency });
        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?> { ["Code"] = "OPS", ["Name"] = "Operations" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?>
            { ["Name"] = "Цех", ["LegalEntity"] = le, ["DivisionType"] = dt });

        // Две должности с разными ставками — расчёт должен различать сотрудников.
        var senior = await Db.InsertAsync("Position", new Dictionary<string, object?> { ["Name"] = "Мастер", ["HourlyRate"] = 50m });
        var junior = await Db.InsertAsync("Position", new Dictionary<string, object?> { ["Name"] = "Ученик", ["HourlyRate"] = 25m });

        var emp1 = await Db.InsertAsync("Employee", new Dictionary<string, object?>
            { ["Name"] = "Иванов", ["Division"] = div, ["Position"] = senior, ["HireDate"] = DateTime.UtcNow.Date, ["IsActive"] = true });
        var emp2 = await Db.InsertAsync("Employee", new Dictionary<string, object?>
            { ["Name"] = "Петров", ["Division"] = div, ["Position"] = junior, ["HireDate"] = DateTime.UtcNow.Date, ["IsActive"] = true });

        return ((Guid)div, (Guid)emp1, (Guid)emp2);
    }

    [IntegrationTest("Команда начисляет ФОТ из часов по ставке должности")]
    public async Task AccruesFromHours()
    {
        var s = await SetupAsync();

        var sheet = await Db.CreateDocumentAsync("TimeSheet",
            new Dictionary<string, object?> { ["Division"] = s.Division },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Lines"] = new[]
                {
                    new Dictionary<string, object?> { ["Employee"] = s.Emp1, ["Hours"] = 10m },
                    new Dictionary<string, object?> { ["Employee"] = s.Emp2, ["Hours"] = 8m },
                },
            },
            subtype: "Approved");

        var commandId = await Db.FindCommandIdAsync("document", "AccruePayroll");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, (Guid)sheet);
        Assert.IsTrue(run.Success, "команда должна выполниться: {0}", run.Message ?? "");

        // 10×50 + 8×25 = 500 + 200 = 700 — суммы нет в табеле, она посчитана по ставкам.
        decimal payroll = 0m;
        foreach (var r in await Db.QueryBalancesAsync("Payroll")) payroll += Convert.ToDecimal(r["Amount"]);
        Assert.IsTrue(payroll == 700m, "ФОТ = 10×50 + 8×25 = 700, факт {0}", payroll);

        decimal liab = 0m;
        foreach (var r in await Db.QueryBalancesAsync("PayrollLiability")) liab += Convert.ToDecimal(r["Amount"]);
        Assert.IsTrue(liab == 700m, "задолженность перед сотрудниками 700, факт {0}", liab);

        Assert.IsTrue(string.Join("; ", run.ClientMessages).Contains("700"),
            "пользователь видит итог начисления: {0}", string.Join("; ", run.ClientMessages));
    }

    [IntegrationTest("Пустой табель ничего не начисляет")]
    public async Task EmptySheetAccruesNothing()
    {
        var s = await SetupAsync();
        var sheet = await Db.CreateDocumentAsync("TimeSheet",
            new Dictionary<string, object?> { ["Division"] = s.Division }, null, subtype: "Approved");

        var commandId = await Db.FindCommandIdAsync("document", "AccruePayroll");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, (Guid)sheet);

        decimal payroll = 0m;
        foreach (var r in await Db.QueryBalancesAsync("Payroll")) payroll += Convert.ToDecimal(r["Amount"]);
        Assert.IsTrue(payroll == 0m, "по пустому табелю начислений нет, факт {0}", payroll);
        Assert.IsTrue(string.Join("; ", run.ClientMessages).Contains("нет строк"),
            "пользователь получил причину: {0}", string.Join("; ", run.ClientMessages));
    }
}
