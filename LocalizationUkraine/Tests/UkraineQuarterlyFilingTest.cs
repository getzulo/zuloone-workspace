using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Runtime.Testing;
using ZuloOne.Services.Contracts;

// Робоча таблиця ЄСВ+ПДФО+ВЗ за період декларації. Не бланк Податкового
// розрахунку ДПС: XML і додатків немає. Перевіряємо файл і згортку.
public class UkraineQuarterlyFilingTest : IntegrationTestScriptBase
{
    private static IDictionaryManager Dict => GetService<IDictionaryManager>();
    private static IDocumentManager Documents => GetService<IDocumentManager>();
    private static IUaTaxFiling Filing => GetService<IUaTaxFiling>();
    private static ITaxReturnService Returns => GetService<ITaxReturnService>();

    [IntegrationTest("Єдиний розрахунок: ЄСВ роботодавця, ПДФО і ВЗ, файл не видає себе за бланк ДПС")]
    public async Task QuarterlyExportCarriesEsvAndLevies()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        await ConfigureUaAsync();

        await AccrueAsync(env.Division, env.Employee, 10000m, new DateTime(2026, 3, 15));

        var returnId = await Returns.BuildAsync(
            env.Entity, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        var exportId = await Filing.ExportQuarterlyAsync(returnId);
        Assert.IsNotNull(exportId, "вивантаження єдиного розрахунку має створитися");

        var text = await PayloadAsync(env.Entity);
        Assert.IsTrue(text.Contains("не бланк ДПС"),
            "файл зобов'язаний чесно назвати себе не бланком ДПС. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("UA-QPR"),
            "тип вивантаження UA-QPR. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("Петренко"),
            "рядок — конкретна людина. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("0.00;2200.00"),
            "ЄСВ роботодавця 22% від 10000, частка працівника 0. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("Разом ЄСВ роботодавець;2200.00"),
            "разом ЄСВ роботодавця 2200. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("10000.00;1800.00"),
            "ПДФО 18% від 10000. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("10000.00;500.00"),
            "ВЗ 5% від 10000. Факт:\n{0}", text);
        Assert.IsTrue(text.Contains("Разом до перенесення;4500.00"),
            "2200+1800+500. Факт:\n{0}", text);
    }

    [IntegrationTest("Квітень не потрапляє у єдиний розрахунок за березень")]
    public async Task OtherMonthIsExcluded()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        await ConfigureUaAsync();

        await AccrueAsync(env.Division, env.Employee, 10000m, new DateTime(2026, 3, 15));
        await AccrueAsync(env.Division, env.Employee, 20000m, new DateTime(2026, 4, 10));

        var returnId = await Returns.BuildAsync(
            env.Entity, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        await Filing.ExportQuarterlyAsync(returnId);

        var text = await PayloadAsync(env.Entity);
        Assert.IsTrue(text.Contains("Разом до перенесення;4500.00"),
            "березень: 2200+1800+500, без квітня. Факт:\n{0}", text);
        Assert.IsTrue(!text.Contains("20000.00"),
            "бази квітня у березневому файлі бути не повинно. Факт:\n{0}", text);
        Assert.IsTrue(!text.Contains("4400.00"),
            "ЄСВ квітня 4400 у березневому файлі бути не повинно. Факт:\n{0}", text);
    }

    [IntegrationTest("Повторна вигрузка заміщує UA-QPR і не чіпає UA-LEVY")]
    public async Task ReExportReplacesQuarterlyOnly()
    {
        var env = await SetupAsync();
        await ConfigureHrAsync();
        await ConfigureUaAsync();

        await AccrueAsync(env.Division, env.Employee, 10000m, new DateTime(2026, 5, 12));
        var from = new DateTime(2026, 5, 1);
        var to = new DateTime(2026, 5, 31);
        var first = await Returns.BuildAsync(env.Entity, from, to);
        await Filing.ExportLeviesAsync(first);
        await Filing.ExportQuarterlyAsync(first);

        await AccrueAsync(env.Division, env.Employee, 10000m, new DateTime(2026, 5, 20));
        var second = await Returns.BuildAsync(env.Entity, from, to);
        await Filing.ExportQuarterlyAsync(second);

        var rows = await ExportsAsync(env.Entity);
        var quarterly = rows.Where(r => r.ReturnType == "UA-QPR").ToList();
        Assert.IsTrue(quarterly.Count == 1, "UA-QPR за період один, факт {0}", quarterly.Count);
        Assert.IsTrue((Convert.ToString(quarterly[0].Payload) ?? "").Contains("Разом до перенесення;9000.00"),
            "свіжі цифри двох нарахувань 4500+4500. Факт:\n{0}", quarterly[0].Payload);

        var levies = rows.Where(r => r.ReturnType == "UA-LEVY").ToList();
        Assert.IsTrue(levies.Count == 1, "UA-LEVY лишився, факт {0}", levies.Count);
        Assert.IsTrue((Convert.ToString(levies[0].Payload) ?? "").Contains("Разом утримано;2300.00"),
            "перша вигрузка утримань не переписана. Факт:\n{0}", levies[0].Payload);
    }

    private async Task<string> PayloadAsync(Guid entity)
    {
        var rows = await ExportsAsync(entity);
        var row = rows.FirstOrDefault(r => r.ReturnType == "UA-QPR");
        Assert.IsTrue(row != null, "рядок UA-QPR має бути");
        return Convert.ToString(row!.Payload) ?? "";
    }

    private static async Task<List<UaTaxFilingExport>> ExportsAsync(Guid entity)
        => await GetService<IDictionaryManager<UaTaxFilingExport>>()
            .GetRecordsAsync($"LegalEntity = '{entity}'");

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
        var codes = Dict;
        var pdfo = (await codes.GetRecordsAsync<TaxCode>("Code = 'UA-PDFO'")).FirstOrDefault();
        Assert.IsTrue(pdfo != null, "код UA-PDFO зобов'язаний бути в поставці");
        var vz = (await codes.GetRecordsAsync<TaxCode>("Code = 'UA-VZ'")).FirstOrDefault();
        Assert.IsTrue(vz != null, "код UA-VZ зобов'язаний бути в поставці");

        var rows = await Dict.GetRecordsAsync<LocalizationUkraineSettings>(null, 1);
        var ua = rows.Count > 0 ? rows[0] : Dict.NewRecord<LocalizationUkraineSettings>();
        ua.IncomeTaxCode = pdfo!.Code;
        ua.MilitaryLevyCode = vz!.Code;
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
