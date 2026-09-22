using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Офіційні бланки ДПС з 01.08.2026 (наказ Мінфіну № 243): J0500111, J0510411, J0510111.
// Нараховано — з регістрів. Виплачено/перераховано не підставляються.
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
        Assert.IsTrue(text.Contains("3123456789;1;1;3;2026;Петренко;31;10000.00;10000.00;0.00;2200.00;1"),
            "рядок Д1. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("R01G20;2200.00"), "разом ЄСВ роботодавця. Факт:\n{0}", text);
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
}
