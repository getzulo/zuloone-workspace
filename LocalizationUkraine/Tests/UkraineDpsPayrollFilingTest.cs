using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Офіційні бланки ДПС з 01.08.2026 (наказ Мінфіну № 243): J0500111, J0510411, J0510111.
// Нараховано — з регістрів. Виплачено — PayrollPayment. Перераховано — UaTaxRemittance.
public class UkraineDpsPayrollFilingTest : IntegrationTestScriptBase
{
    private static IDictionaryManager Dict => GetService<IDictionaryManager>();
    private static IDocumentManager Documents => GetService<IDocumentManager>();
    private static IUaTaxFiling Filing => GetService<IUaTaxFiling>();
    private static ITaxReturnService Returns => GetService<ITaxReturnService>();

    [IntegrationTest("4ДФ J0510411: ознака 101, нараховано 10000/1800/500, виплачено 0")]
    public async Task FourDfCarriesOfficialCells()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        await ConfigureUaAsync();
        await AccrueAsync(env.Division, env.Employee, 10000m, new DateTime(2026, 3, 15));

        var returnId = await Returns.BuildAsync(env.Entity, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        await Filing.ExportDpsPayrollAsync(returnId);

        var text = await PayloadAsync(env.Entity, "J0510411");
        Assert.IsTrue(text.Contains("J0510411"), "тип форми. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("Код ДПІ;26;01"), "C_REG/C_STI з юрлица. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("виплачено/перераховано порожні") || text.Contains("Графи виплачено"),
            "файл не видає виплату, якої не було. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("3123456789"), "РНОКПП. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("10000.00;0.00;1800.00;0.00;500.00;0.00;101"),
            "графи 3а/3/4а/4/5а/5/ознака. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("R01G03A;10000.00"), "разом нарахованого доходу. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("R01G03;0.00"), "разом виплаченого нуль. Факт:\n{0}", text);
    }

    [IntegrationTest("Розрахунок J0500111: зарплата 10000, ЄСВ 2200")]
    public async Task CalculationSectionI()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        await ConfigureUaAsync();
        await AccrueAsync(env.Division, env.Employee, 10000m, new DateTime(2026, 3, 15));

        var returnId = await Returns.BuildAsync(env.Entity, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        await Filing.ExportDpsPayrollAsync(returnId);

        var text = await PayloadAsync(env.Entity, "J0500111");
        Assert.IsTrue(text.Contains("R01011G3;Сума нарахованої заробітної плати;10000.00"),
            "рядок 1.1. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("R01031G3;Рядок 2.1 × 22 %;2200.00"),
            "ЄСВ 22%. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("R0107G3;Єдиний внесок до сплати;2200.00"),
            "до сплати. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("R061G3;1") && text.Contains("R064G3;1"),
            "відмітки додатків Д1 і 4ДФ. Факт:\n{0}", text);
        Assert.IsTrue(!text.Contains("R065G3") && !text.Contains("R066G3"),
            "без кадрових подій Д5/Д6 не відмічають. Факт:\n{0}", text);
        Assert.IsTrue(!text.Contains("R062G3") && !text.Contains("R063G3"),
            "Д2/Д3 звичайний роботодавець не подає. Факт:\n{0}", text);
    }

    [IntegrationTest("Д1 J0510111: категорія 1, база 10000, ЄСВ роботодавця 2200")]
    public async Task D1InsuredPerson()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        await ConfigureUaAsync();
        await AccrueAsync(env.Division, env.Employee, 10000m, new DateTime(2026, 3, 15));

        var returnId = await Returns.BuildAsync(env.Entity, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        await Filing.ExportDpsPayrollAsync(returnId);

        var text = await PayloadAsync(env.Entity, "J0510111");
        Assert.IsTrue(text.Contains("3123456789;1;1;3;2026;Петренко;31;10000.00;10000.00;0.00;2200.00;1;0"),
            "рядок Д1. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("R01G20;2200.00"), "разом ЄСВ роботодавця. Факт:\n{0}", text);
    }

    [IntegrationTest("4ДФ J0510411: після виплати ФОТ графа 3 = 10000")]
    public async Task FourDfPaidIncomeAfterPayrollPayment()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        await ConfigureUaAsync();
        await AccrueAsync(env.Division, env.Employee, 10000m, new DateTime(2026, 3, 15));
        await PayAsync(env.Employee, 7700m, new DateTime(2026, 3, 20));

        var returnId = await Returns.BuildAsync(env.Entity, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        await Filing.ExportDpsPayrollAsync(returnId);

        var text = await PayloadAsync(env.Entity, "J0510411");
        Assert.IsTrue(text.Contains("10000.00;10000.00;1800.00;0.00;500.00;0.00;101"),
            "виплачено дохід, податок ще в бюджеті. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("R01G03;10000.00"), "разом виплаченого. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("R01G04;0.00"), "ПДФО ще не перераховано. Факт:\n{0}", text);
    }

    [IntegrationTest("4ДФ J0510411: після перерахування G04=1800 G5=500")]
    public async Task FourDfTransferredAfterRemittance()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        await ConfigureUaAsync();
        await AccrueAsync(env.Division, env.Employee, 10000m, new DateTime(2026, 3, 15));
        await PayAsync(env.Employee, 7700m, new DateTime(2026, 3, 20));
        await RemitAsync(env.Entity, env.Employee, "UA-PDFO", 1800m, new DateTime(2026, 3, 25));
        await RemitAsync(env.Entity, env.Employee, "UA-VZ", 500m, new DateTime(2026, 3, 25));

        var returnId = await Returns.BuildAsync(env.Entity, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        await Filing.ExportDpsPayrollAsync(returnId);

        var text = await PayloadAsync(env.Entity, "J0510411");
        Assert.IsTrue(text.Contains("10000.00;10000.00;1800.00;1800.00;500.00;500.00;101"),
            "повний рядок 4ДФ після виплати і сплати. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("R01G04;1800.00"), "разом ПДФО. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("R01G5;500.00"), "разом ВЗ. Факт:\n{0}", text);
    }

    [IntegrationTest("J0500111 і Д1: після сплати ЄСВ G20 лишається 2200, R0107 = 0")]
    public async Task D1AccruedStaysAfterEsvPayment()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        await ConfigureUaAsync();
        await AccrueAsync(env.Division, env.Employee, 10000m, new DateTime(2026, 3, 15));
        await PayEsvAsync(env.Division, env.Employee, 2200m, new DateTime(2026, 3, 20));

        var returnId = await Returns.BuildAsync(env.Entity, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        await Filing.ExportDpsPayrollAsync(returnId);

        var d1 = await PayloadAsync(env.Entity, "J0510111");
        Assert.IsTrue(d1.Contains("3123456789;1;1;3;2026;Петренко;31;10000.00;10000.00;0.00;2200.00;1;0"),
            "Д1 нараховане ЄСВ не з'їдає сплата. Факт:\n{0}", d1);
        Assert.IsTrue(d1.Contains("R01G20;2200.00"), "разом G20 лишається. Факт:\n{0}", d1);

        var calc = await PayloadAsync(env.Entity, "J0500111");
        Assert.IsTrue(calc.Contains("R01031G3;Рядок 2.1 × 22 %;2200.00"),
            "нараховано лишається. Факт:\n{0}", calc);
        Assert.IsTrue(calc.Contains("R0104G3;Перераховано єдиного внеску;2200.00"),
            "сплачено з платежу в фонд. Факт:\n{0}", calc);
        Assert.IsTrue(calc.Contains("R0107G3;Єдиний внесок до сплати;0.00"),
            "до сплати нуль. Факт:\n{0}", calc);
    }

    [IntegrationTest("4ДФ J0510411: ознака 102 з картки працівника")]
    public async Task FourDfIncomeSignFromEmployee()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        await ConfigureUaAsync();
        await Db.UpdateAsync("Employee", env.Employee, new Dictionary<string, object?>
        {
            ["DpsIncomeSign"] = "102",
            ["TaxCardNumber"] = "3123456789",
            ["Name"] = "Петренко",
        });
        await AccrueAsync(env.Division, env.Employee, 10000m, new DateTime(2026, 3, 15));

        var returnId = await Returns.BuildAsync(env.Entity, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        await Filing.ExportDpsPayrollAsync(returnId);

        var text = await PayloadAsync(env.Entity, "J0510411");
        Assert.IsTrue(text.Contains("10000.00;0.00;1800.00;0.00;500.00;0.00;102"),
            "ознака з працівника, не з одиночних налаштувань. Факт:\n{0}", text);
    }

    [IntegrationTest("Д5 J0510511: прийом 12.03.2026, R065, без Д2/Д3")]
    public async Task D5HireInPeriod()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        await ConfigureUaAsync();
        await PatchEmployeeAsync(env, hire: new DateTime(2026, 3, 12));
        await AccrueAsync(env.Division, env.Employee, 10000m, new DateTime(2026, 3, 15));

        var returnId = await Returns.BuildAsync(env.Entity, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        await Filing.ExportDpsPayrollAsync(returnId);

        var d5 = await PayloadAsync(env.Entity, "J0510511");
        Assert.IsTrue(d5.Contains("J0510511"), "тип Д5. Факт:\n{0}", d5);
        Assert.IsTrue(d5.Contains("1;0;1;3123456789;Петренко;12032026;0;0;"),
            "рядок прийому категорія 1. Факт:\n{0}", d5);

        var calc = await PayloadAsync(env.Entity, "J0500111");
        Assert.IsTrue(calc.Contains("R065G3;1"), "відмітка Д5. Факт:\n{0}", calc);
        Assert.IsTrue(!calc.Contains("R062G3") && !calc.Contains("R063G3"),
            "Д2/Д3 не відмічені. Факт:\n{0}", calc);

        var four = await PayloadAsync(env.Entity, "J0510411");
        Assert.IsTrue(four.Contains("12.03.2026"), "4ДФ дата прийняття. Факт:\n{0}", four);
    }

    [IntegrationTest("Д5 J0510511: звільнення 20.03.2026 і підстава КЗпП")]
    public async Task D5FireInPeriod()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        await ConfigureUaAsync();
        await PatchEmployeeAsync(env, fire: new DateTime(2026, 3, 20), fireReason: "п. 1 ч. 1 ст. 36 КЗпП");
        await AccrueAsync(env.Division, env.Employee, 10000m, new DateTime(2026, 3, 15));

        var returnId = await Returns.BuildAsync(env.Entity, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        await Filing.ExportDpsPayrollAsync(returnId);

        var d5 = await PayloadAsync(env.Entity, "J0510511");
        Assert.IsTrue(d5.Contains("1;0;1;3123456789;Петренко;20032026;0;0;"),
            "рядок звільнення. Факт:\n{0}", d5);
        Assert.IsTrue(d5.Contains("п. 1 ч. 1 ст. 36 КЗпП"), "підстава. Факт:\n{0}", d5);

        var four = await PayloadAsync(env.Entity, "J0510411");
        Assert.IsTrue(four.Contains("20.03.2026"), "4ДФ дата звільнення. Факт:\n{0}", four);
    }

    [IntegrationTest("Д5 категорія 6 з TimeOff, Д6 код спецстажу, Д1 G23=1")]
    public async Task D5LeaveAndD6Tenure()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        await ConfigureUaAsync();
        await PatchEmployeeAsync(env, tenure: "10100000");

        var off = Dict.NewRecord<TimeOff>();
        off.Name = "Догляд до 3 років";
        off.Employee = env.Employee;
        off.Kind = AttendanceDayKind.Absence;
        off.DateFrom = new DateTime(2026, 3, 5);
        off.DateTo = new DateTime(2026, 6, 5);
        off = await Dict.SaveRecordAsync(off);
        await Db.UpdateAsync("TimeOff", off.MetaId, new Dictionary<string, object?>
        {
            ["DpsPersonCategory"] = "6",
            ["Employee"] = env.Employee,
            ["Kind"] = (int)AttendanceDayKind.Absence,
            ["DateFrom"] = new DateTime(2026, 3, 5),
            ["DateTo"] = new DateTime(2026, 6, 5),
            ["Name"] = "Догляд до 3 років",
        });

        await AccrueAsync(env.Division, env.Employee, 10000m, new DateTime(2026, 3, 15));

        var returnId = await Returns.BuildAsync(env.Entity, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        await Filing.ExportDpsPayrollAsync(returnId);

        var d5 = await PayloadAsync(env.Entity, "J0510511");
        Assert.IsTrue(d5.Contains(";6;3123456789;Петренко;05032026;"),
            "початок відпустки категорія 6. Факт:\n{0}", d5);

        var d6 = await PayloadAsync(env.Entity, "J0510611");
        Assert.IsTrue(d6.Contains("10100000"), "код спецстажу. Факт:\n{0}", d6);
        Assert.IsTrue(d6.Contains("01032026;31032026;31"), "період березень. Факт:\n{0}", d6);

        var d1 = await PayloadAsync(env.Entity, "J0510111");
        Assert.IsTrue(d1.Contains("2200.00;1;1"), "Д1 G23 спецстаж. Факт:\n{0}", d1);

        var calc = await PayloadAsync(env.Entity, "J0500111");
        Assert.IsTrue(calc.Contains("R065G3;1") && calc.Contains("R066G3;1"),
            "відмітки Д5 і Д6. Факт:\n{0}", calc);
    }

    private async Task PatchEmployeeAsync(
        (Guid Division, Guid Entity, Guid Employee) env,
        DateTime? hire = null,
        DateTime? fire = null,
        string? fireReason = null,
        string? tenure = null)
    {
        var bag = new Dictionary<string, object?>
        {
            ["TaxCardNumber"] = "3123456789",
            ["Division"] = env.Division,
            ["HireDate"] = hire ?? new DateTime(2024, 1, 1),
            ["Name"] = "Петренко",
            ["IsActive"] = fire == null,
        };
        if (fire != null) bag["FireDate"] = fire;
        if (fireReason != null) bag["FireReason"] = fireReason;
        if (tenure != null) bag["DpsTenureGround"] = tenure;
        var employee = (await Dict.GetRecordsAsync<Employee>($"Division = '{env.Division}'"))
            .FirstOrDefault(e => e.MetaId == env.Employee);
        if (employee != null)
        {
            bag["Position"] = employee.Position;
            bag["Nationality"] = employee.Nationality;
        }
        await Db.UpdateAsync("Employee", env.Employee, bag);
    }

    private async Task PayEsvAsync(Guid division, Guid employee, decimal employer, DateTime on)
    {
        var doc = await Documents.NewDocumentAsync<SocialInsurancePayment>();
        doc.Division = division;
        doc.DocumentDate = on;
        doc.Lines.Add(new SocialInsurancePaymentLinesTablePartRow
        {
            Employee = employee,
            EmployeeContribution = 0m,
            EmployerContribution = employer,
        });
        await Documents.SaveDocumentAsync(doc);
        var commandId = await Db.FindCommandIdAsync("document", "PaySocialInsurance");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, doc.MetaId);
        Assert.IsTrue(run.Success, "сплата ЄСВ: {0}", run.Message ?? "");
    }

    private async Task<string> PayloadAsync(Guid entity, string returnType)
    {
        var rows = await GetService<IDictionaryManager<UaTaxFilingExport>>()
            .GetRecordsAsync($"LegalEntity = '{entity}'");
        var row = rows.FirstOrDefault(r => r.ReturnType == returnType);
        Assert.IsTrue(row != null, "рядок {0} має бути", returnType);
        return Convert.ToString(row!.Payload) ?? "";
    }

    private async Task<(Guid Division, Guid Entity, Guid Employee)> SetupAsync()
    {
        var currency = Dict.NewRecord<Currency>();
        currency.Name = "Hryvnia";
        currency.Code = $"U{Db.NewId():N}"[..3].ToUpperInvariant();
        currency.Symbol = "₴";
        currency = await Dict.SaveRecordAsync(currency);

        var country = Dict.NewRecord<Country>();
        country.Name = "Ukraine";
        country.CodeISO2 = $"{Db.NewId():N}"[..2].ToUpperInvariant();
        country.CodeISO3 = $"{Db.NewId():N}"[..3].ToUpperInvariant();
        country.PhoneCode = "380";
        country = await Dict.SaveRecordAsync(country);

        var entity = Dict.NewRecord<LegalEntity>();
        entity.Name = "ТОВ Тест";
        entity.RegistrationNumber = $"REG-{Db.NewId():N}"[..12];
        entity.TaxRegistrationNumber = "123456789012";
        entity.Country = country.MetaId;
        entity.Currency = currency.MetaId;
        entity = await Dict.SaveRecordAsync(entity);
        await Db.UpdateAsync("LegalEntity", entity.MetaId, new Dictionary<string, object?>
        {
            ["DpsRegionCode"] = "26",
            ["DpsOfficeCode"] = "01",
            ["Country"] = country.MetaId,
            ["Currency"] = currency.MetaId,
            ["Name"] = "ТОВ Тест",
            ["RegistrationNumber"] = entity.RegistrationNumber,
            ["TaxRegistrationNumber"] = "123456789012",
        });

        var divisionType = Dict.NewRecord<DivisionType>();
        divisionType.Code = $"HQ-{Db.NewId():N}"[..12];
        divisionType.Name = "Head office";
        divisionType = await Dict.SaveRecordAsync(divisionType);

        var division = Dict.NewRecord<Division>();
        division.Name = "Київ";
        division.LegalEntity = entity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await Dict.SaveRecordAsync(division);

        var position = Dict.NewRecord<Position>();
        position.Name = $"Dev-{Db.NewId():N}"[..12];
        position.HourlyRate = 50m;
        position = await Dict.SaveRecordAsync(position);

        var employee = Dict.NewRecord<Employee>();
        employee.Name = "Петренко";
        employee.Division = division.MetaId;
        employee.Position = position.MetaId;
        employee.HireDate = new DateTime(2024, 1, 1);
        employee.IsActive = true;
        employee.Nationality = country.MetaId;
        employee = await Dict.SaveRecordAsync(employee);

        await Db.UpdateAsync("Employee", employee.MetaId, new Dictionary<string, object?>
        {
            ["TaxCardNumber"] = "3123456789",
            ["Division"] = division.MetaId,
            ["HireDate"] = new DateTime(2024, 1, 1),
            ["Name"] = "Петренко",
            ["Position"] = position.MetaId,
            ["IsActive"] = true,
            ["Nationality"] = country.MetaId,
        });

        return (division.MetaId, entity.MetaId, employee.MetaId);
    }

    private async Task ConfigureHrAsync()
    {
        var settings = Dict.NewRecord<HRSettings>();
        settings.PayrollRunDay = 25;
        settings.WorkHoursPerDay = 8m;
        settings.SocialInsuranceEmployeeRate = 0m;
        settings.SocialInsuranceEmployerRate = 0.22m;
        settings.SocialInsuranceForeignEmployerRate = 0.22m;
        settings.SocialInsuranceWageCeiling = 120000m;
        await Dict.SaveRecordAsync(settings);
    }

    private async Task ConfigureUaAsync()
    {
        var pdfo = (await Dict.GetRecordsAsync<TaxCode>("Code = 'UA-PDFO'")).FirstOrDefault();
        Assert.IsTrue(pdfo != null, "код UA-PDFO зобов'язаний бути в поставці");
        var vz = (await Dict.GetRecordsAsync<TaxCode>("Code = 'UA-VZ'")).FirstOrDefault();
        Assert.IsTrue(vz != null, "код UA-VZ зобов'язаний бути в поставці");

        var rows = await Dict.GetRecordsAsync<LocalizationUkraineSettings>(null, 1);
        var ua = rows.Count > 0 ? rows[0] : Dict.NewRecord<LocalizationUkraineSettings>();
        ua.IncomeTaxCode = pdfo!.Code;
        ua.MilitaryLevyCode = vz!.Code;
        ua.PayrollIncomeSign = "101";
        await Dict.SaveRecordAsync(ua);
    }

    private async Task AccrueAsync(Guid division, Guid employee, decimal amount, DateTime on)
    {
        var doc = await Documents.NewDocumentAsync<PayrollAccrual>();
        doc.Division = division;
        doc.DocumentDate = on;
        doc.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = employee, Amount = amount });
        await Documents.SaveDocumentAsync(doc);
        doc.Subtype = PayrollAccrual.Subtypes.Posted;
        await Documents.SaveDocumentAsync(doc);
    }

    private async Task PayAsync(Guid employee, decimal amount, DateTime on)
    {
        var doc = await Documents.NewDocumentAsync<PayrollPayment>();
        doc.DocumentDate = on;
        doc.Lines.Add(new PayrollPaymentLinesTablePartRow { Employee = employee, Amount = amount });
        await Documents.SaveDocumentAsync(doc);
        var commandId = await Db.FindCommandIdAsync("document", "PayPayroll");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, doc.MetaId);
        Assert.IsTrue(run.Success, "виплата ФОТ: {0}", run.Message ?? "");
    }

    private async Task RemitAsync(Guid entity, Guid employee, string taxCode, decimal amount, DateTime on)
    {
        var code = (await Dict.GetRecordsAsync<TaxCode>($"Code = '{taxCode}'")).FirstOrDefault();
        Assert.IsTrue(code != null, "код {0} зобов'язаний бути в поставці", taxCode);

        var doc = await Documents.NewDocumentAsync<UaTaxRemittance>();
        doc.LegalEntity = entity;
        doc.TaxCode = code!.MetaId;
        doc.DocumentDate = on;
        doc.Lines.Add(new UaTaxRemittanceLinesTablePartRow { Employee = employee, Amount = amount });
        await Documents.SaveDocumentAsync(doc);
        var commandId = await Db.FindCommandIdAsync("document", "UaPayTaxRemittance");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, doc.MetaId);
        Assert.IsTrue(run.Success, "перерахування {0}: {1}", taxCode, run.Message ?? "");
    }
}
