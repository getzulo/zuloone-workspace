using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

// Соцстрах: проведённое начисление ФОТ порождает начисление взносов.
// Проверяем арифметику (ставки работника/работодателя), потолок базы,
// разные ставки для граждан и иностранцев и необязательность контура.
public class SocialInsuranceTest : IntegrationTestScriptBase
{
    private async Task<(Guid Division, Guid Home, Guid Foreign)> SetupAsync()
    {
        var currency = await Db.InsertAsync("Currency", new Dictionary<string, object?>
            { ["Name"] = "Saudi Riyal", ["Code"] = "SAR", ["Symbol"] = "﷼" });
        var home = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Saudi Arabia", ["CodeISO2"] = "SA", ["CodeISO3"] = "SAU", ["PhoneCode"] = "966" });
        var foreign = await Db.InsertAsync("Country", new Dictionary<string, object?>
            { ["Name"] = "Egypt", ["CodeISO2"] = "EG", ["CodeISO3"] = "EGY", ["PhoneCode"] = "20" });
        var le = await Db.InsertAsync("LegalEntity", new Dictionary<string, object?>
            { ["Name"] = "ACME KSA", ["RegistrationNumber"] = "REG-SI-1", ["Country"] = home, ["Currency"] = currency });
        var dt = await Db.InsertAsync("DivisionType", new Dictionary<string, object?>
            { ["Code"] = $"HQ-{Db.NewId():N}"[..12], ["Name"] = "Head office" });
        var div = await Db.InsertAsync("Division", new Dictionary<string, object?>
            { ["Name"] = "HQ", ["LegalEntity"] = le, ["DivisionType"] = dt });

        return ((Guid)div, (Guid)home, (Guid)foreign);
    }

    /// <summary>Ставки КСА (GOSI): 9.75% работник, 11.75% работодатель, 2% за иностранцев, потолок 45 000.</summary>
    private async Task ConfigureAsync(Guid home, decimal ceiling = 45000m)
        => await Db.InsertAsync("HRSettings", new Dictionary<string, object?>
        {
            ["PayrollRunDay"] = 25,
            ["WorkHoursPerDay"] = 8m,
            ["HomeCountry"] = home,
            ["SocialInsuranceEmployeeRate"] = 0.0975m,
            ["SocialInsuranceEmployerRate"] = 0.1175m,
            ["SocialInsuranceForeignEmployerRate"] = 0.02m,
            ["SocialInsuranceWageCeiling"] = ceiling,
        });

    private async Task<Guid> NewEmployeeAsync(Guid division, Guid? nationality, string name)
    {
        var pos = await Db.InsertAsync("Position", new Dictionary<string, object?>
            { ["Name"] = $"Dev-{Db.NewId():N}"[..12], ["HourlyRate"] = 50m });
        var fields = new Dictionary<string, object?>
            { ["Name"] = name, ["Division"] = division, ["Position"] = pos, ["HireDate"] = new DateTime(2024, 1, 1), ["IsActive"] = true };
        if (nationality.HasValue) fields["Nationality"] = nationality.Value;
        return (Guid)await Db.InsertAsync("Employee", fields);
    }

    // Создаём в исходном подтипе и переводим отдельным шагом: подтип Posted
    // заперт (isReadOnly), строки в него сразу записать нельзя.
    private async Task<Guid> AccrueAsync(Guid division, IEnumerable<(Guid Employee, decimal Amount)> lines)
    {
        var doc = await Db.CreateDocumentAsync("PayrollAccrual",
            new Dictionary<string, object?> { ["Division"] = division },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Lines"] = lines.Select(l => new Dictionary<string, object?>
                    { ["Employee"] = l.Employee, ["Amount"] = l.Amount }).ToArray(),
            });
        await Db.ChangeSubtypeAsync("PayrollAccrual", doc, "Posted");
        return (Guid)doc;
    }

    [IntegrationTest("Начисление ФОТ порождает взносы по ставкам гражданина")]
    public async Task AccrualCreatesLocalContributions()
    {
        var s = await SetupAsync();
        await ConfigureAsync(s.Home);
        var emp = await NewEmployeeAsync(s.Division, s.Home, "Ahmed");

        // 10 000 × 9.75% = 975 работник; × 11.75% = 1175 работодатель.
        var pa = await AccrueAsync(s.Division, new[] { (emp, 10000m) });

        var si = await Db.QueryAsync("SocialInsuranceAccrual", null);
        Assert.IsTrue(si.Count == 1, "должно появиться одно начисление взносов, факт {0}", si.Count);

        var lines = await Db.QueryAsync("TP_SocialInsuranceAccrualLines", $"OwnerMetaId = '{si[0]["MetaId"]}'");
        Assert.IsTrue(lines.Count == 1, "одна строка взносов, факт {0}", lines.Count);
        Assert.IsTrue(Convert.ToDecimal(lines[0]["EmployeeContribution"]) == 975m,
            "взнос работника = 10000 × 9.75% = 975, факт {0}", lines[0]["EmployeeContribution"]);
        Assert.IsTrue(Convert.ToDecimal(lines[0]["EmployerContribution"]) == 1175m,
            "взнос работодателя = 10000 × 11.75% = 1175, факт {0}", lines[0]["EmployerContribution"]);

        // Взносы проведены в регистр, а не остались черновиком.
        decimal regEmployee = 0m, regEmployer = 0m;
        foreach (var r in await Db.QueryBalancesAsync("SocialInsurance"))
        {
            regEmployee += Convert.ToDecimal(r["EmployeeContribution"]);
            regEmployer += Convert.ToDecimal(r["EmployerContribution"]);
        }
        Assert.IsTrue(regEmployee == 975m, "регистр: взнос работника 975, факт {0}", regEmployee);
        Assert.IsTrue(regEmployer == 1175m, "регистр: взнос работодателя 1175, факт {0}", regEmployer);

        var edges = await Db.GetDocumentFamilyEdgesAsync(pa);
        Assert.IsTrue(edges.Count > 0, "взносы связаны с начислением ФОТ");
    }

    [IntegrationTest("За иностранца платит только работодатель и по своей ставке")]
    public async Task ForeignStaffPaysEmployerRateOnly()
    {
        var s = await SetupAsync();
        await ConfigureAsync(s.Home);
        var emp = await NewEmployeeAsync(s.Division, s.Foreign, "Mahmoud");

        await AccrueAsync(s.Division, new[] { (emp, 10000m) });

        var si = await Db.QueryAsync("SocialInsuranceAccrual", null);
        var lines = await Db.QueryAsync("TP_SocialInsuranceAccrualLines", $"OwnerMetaId = '{si[0]["MetaId"]}'");
        Assert.IsTrue(Convert.ToDecimal(lines[0]["EmployeeContribution"]) == 0m,
            "с иностранца взнос работника не удерживается, факт {0}", lines[0]["EmployeeContribution"]);
        Assert.IsTrue(Convert.ToDecimal(lines[0]["EmployerContribution"]) == 200m,
            "взнос работодателя = 10000 × 2% = 200, факт {0}", lines[0]["EmployerContribution"]);
    }

    [IntegrationTest("Потолок базы ограничивает взнос, дробление строк его не обходит")]
    public async Task CeilingCapsContribution()
    {
        var s = await SetupAsync();
        await ConfigureAsync(s.Home, ceiling: 45000m);
        var emp = await NewEmployeeAsync(s.Division, s.Home, "Salim");

        // Две строки по 30 000 = 60 000 начислений одному сотруднику. Взнос
        // считается с суммы, ограниченной потолком: 45 000 × 9.75% = 4387.5.
        await AccrueAsync(s.Division, new[] { (emp, 30000m), (emp, 30000m) });

        var si = await Db.QueryAsync("SocialInsuranceAccrual", null);
        var lines = await Db.QueryAsync("TP_SocialInsuranceAccrualLines", $"OwnerMetaId = '{si[0]["MetaId"]}'");
        Assert.IsTrue(lines.Count == 1, "строки одного сотрудника схлопываются в одну, факт {0}", lines.Count);
        Assert.IsTrue(Convert.ToDecimal(lines[0]["ContributoryBase"]) == 45000m,
            "база ограничена потолком 45000, факт {0}", lines[0]["ContributoryBase"]);
        Assert.IsTrue(Convert.ToDecimal(lines[0]["EmployeeContribution"]) == 4387.5m,
            "взнос работника = 45000 × 9.75% = 4387.5, факт {0}", lines[0]["EmployeeContribution"]);
    }

    [IntegrationTest("Без настроек соцстраха начисление ФОТ проводится как раньше")]
    public async Task NoSettingsStillAccrues()
    {
        var s = await SetupAsync();
        // ConfigureAsync НЕ вызываем: настроек соцстраха нет.
        var emp = await NewEmployeeAsync(s.Division, s.Home, "Nadia");

        var pa = await AccrueAsync(s.Division, new[] { (emp, 10000m) });

        var doc = await Db.GetAsync("PayrollAccrual", pa);
        Assert.IsTrue((doc?["Subtype"] as string) == "Posted",
            "ФОТ проведён несмотря на ненастроенный соцстрах, факт {0}", doc?["Subtype"]);
        Assert.IsTrue((await Db.QueryAsync("SocialInsuranceAccrual", null)).Count == 0,
            "начисление взносов не создано");

        // И сам ФОТ отработал: задолженность перед сотрудником признана.
        decimal liability = 0m;
        foreach (var r in await Db.QueryBalancesAsync("PayrollLiability")) liability += Convert.ToDecimal(r["Amount"]);
        Assert.IsTrue(liability == 10000m, "задолженность 10000, факт {0}", liability);
    }
}
