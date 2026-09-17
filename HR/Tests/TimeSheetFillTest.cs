using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

public class TimeSheetFillTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    // Mon–Fri 14–18 Sep 2026. Five working days × 8h = 40h week.
    private static readonly DateTime WeekFrom = new(2026, 9, 14);
    private static readonly DateTime WeekTo = new(2026, 9, 18);

    private async Task<(Guid Division, Guid Emp1, Guid Emp2)> SetupAsync()
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Euro";
        currency.Code = "EUR";
        currency.Symbol = "€";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Germany";
        country.CodeISO2 = "DE";
        country.CodeISO3 = "DEU";
        country.PhoneCode = "49";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "ACME GmbH";
        legalEntity.RegistrationNumber = "REG-TS-FILL-1";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = "OPS";
        divisionType.Name = "Operations";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Цех";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var position = DictionaryManager.NewRecord<Position>();
        position.Name = "Мастер";
        position.HourlyRate = 50m;
        position = await DictionaryManager.SaveRecordAsync(position);

        var emp1 = DictionaryManager.NewRecord<Employee>();
        emp1.Name = "Иванов";
        emp1.Division = division.MetaId;
        emp1.Position = position.MetaId;
        emp1.HireDate = new DateTime(2026, 1, 1);
        emp1.IsActive = true;
        emp1 = await DictionaryManager.SaveRecordAsync(emp1);

        var emp2 = DictionaryManager.NewRecord<Employee>();
        emp2.Name = "Петров";
        emp2.Division = division.MetaId;
        emp2.Position = position.MetaId;
        emp2.HireDate = new DateTime(2026, 1, 1);
        emp2.IsActive = true;
        emp2 = await DictionaryManager.SaveRecordAsync(emp2);

        return (division.MetaId, emp1.MetaId, emp2.MetaId);
    }

    private async Task<TimeSheet> DraftSheetAsync(Guid division)
    {
        var sheet = await DocumentManager.NewDocumentAsync<TimeSheet>();
        sheet.Division = division;
        sheet.PeriodFrom = WeekFrom;
        sheet.PeriodTo = WeekTo;
        await DocumentManager.SaveDocumentAsync(sheet);
        return sheet;
    }

    private async Task<TimeSheet> FillAsync(Guid sheetId)
    {
        var commandId = await Db.FindCommandIdAsync("document", "FillTimeSheet");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, sheetId);
        Assert.IsTrue(run.Success, "заполнение должно пройти: {0}", run.Message ?? "");
        var sheet = await DocumentManager.GetDocumentAsync<TimeSheet>(sheetId);
        Assert.IsTrue(sheet != null, "табель должен остаться после заполнения");
        return sheet!;
    }

    [IntegrationTest("Заполнение ставит 40 часов за неделю каждому сотруднику подразделения")]
    public async Task FillsStandardWeek()
    {
        var s = await SetupAsync();
        var emps = await DictionaryManager.GetRecordsAsync<Employee>($"Division = '{s.Division}'");
        Assert.IsTrue(emps.Count >= 2, "сотрудники подразделения видны запросу, факт {0}", emps.Count);

        var sheet = await FillAsync((await DraftSheetAsync(s.Division)).MetaId);
        Assert.IsTrue(sheet.Days.Count == 10, "5 рабочих дней × 2 сотрудника = 10, факт {0}", sheet.Days.Count);
        Assert.IsTrue(sheet.Days.All(d => d.DayKind == AttendanceDayKind.Work && d.IsAbsent != true && d.Hours == 8m),
            "автодни — явка по 8 часов");
        Assert.IsTrue(sheet.Lines.Count == 2, "итог по двум сотрудникам, факт {0}", sheet.Lines.Count);
        Assert.IsTrue(sheet.Lines.All(l => l.Hours == 40m), "40 часов за неделю, факт {0}",
            string.Join(",", sheet.Lines.Select(l => l.Hours)));
    }

    [IntegrationTest("Отпуск вычитается из часов и переживает повторное заполнение")]
    public async Task TimeOffCutsHours()
    {
        var s = await SetupAsync();

        var off = DictionaryManager.NewRecord<TimeOff>();
        off.Employee = s.Emp1;
        off.Kind = AttendanceDayKind.Vacation;
        off.DateFrom = new DateTime(2026, 9, 16);
        off.DateTo = new DateTime(2026, 9, 17);
        off.Name = "Отпуск Иванова";
        await DictionaryManager.SaveRecordAsync(off);

        var sheet = await FillAsync((await DraftSheetAsync(s.Division)).MetaId);
        var emp1Days = sheet.Days.Where(d => d.Employee == s.Emp1).OrderBy(d => d.WorkDate).ToList();
        Assert.IsTrue(emp1Days.Count == 5, "у Иванова 5 дней сетки");
        Assert.IsTrue(emp1Days[2].DayKind == AttendanceDayKind.Vacation && emp1Days[2].Hours == 0m,
            "среда — отпуск");
        Assert.IsTrue(emp1Days[3].DayKind == AttendanceDayKind.Vacation && emp1Days[3].Hours == 0m,
            "четверг — отпуск");
        var emp1Hours = sheet.Lines.Single(l => l.Employee == s.Emp1).Hours;
        Assert.IsTrue(emp1Hours == 24m, "3 рабочих дня × 8 = 24, факт {0}", emp1Hours);

        sheet = await FillAsync(sheet.MetaId);
        emp1Hours = sheet.Lines.Single(l => l.Employee == s.Emp1).Hours;
        Assert.IsTrue(emp1Hours == 24m, "повторное заполнение не затирает отпуск, факт {0}", emp1Hours);
    }

    [IntegrationTest("Ручная галка отсутствия не затирается повторным заполнением")]
    public async Task ManualTickSurvivesRefill()
    {
        var s = await SetupAsync();
        var sheet = await FillAsync((await DraftSheetAsync(s.Division)).MetaId);

        var friday = sheet.Days.Single(d => d.Employee == s.Emp2 && DayOf(d.WorkDate) == new DateTime(2026, 9, 18));
        friday.IsAbsent = true;
        friday.DayKind = AttendanceDayKind.Absence;
        friday.Hours = 0m;
        friday.IsManual = true;
        await DocumentManager.SaveDocumentAsync(sheet);

        sheet = await FillAsync(sheet.MetaId);
        friday = sheet.Days.Single(d => d.Employee == s.Emp2 && DayOf(d.WorkDate) == new DateTime(2026, 9, 18));
        Assert.IsTrue(friday.IsManual == true && friday.IsAbsent == true && friday.Hours == 0m,
            "ручная пятница осталась отсутствием");
        var emp2Hours = sheet.Lines.Single(l => l.Employee == s.Emp2).Hours;
        Assert.IsTrue(emp2Hours == 32m, "4 дня × 8 = 32, факт {0}", emp2Hours);
    }

    [IntegrationTest("Команда заполнения видна на черновике и вызывает сервис")]
    public async Task FillCommandRuns()
    {
        var s = await SetupAsync();
        var draft = await DraftSheetAsync(s.Division);
        var commandId = await Db.FindCommandIdAsync("document", "FillTimeSheet");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, draft.MetaId);
        Assert.IsTrue(run.Success, "команда должна выполниться: {0}", run.Message ?? "");
        Assert.IsTrue(string.Join("; ", run.ClientMessages).Contains("Заполнено"),
            "пользователь видит итог: {0}", string.Join("; ", run.ClientMessages));
    }

    private static DateTime? DayOf(object? value)
        => value is DateTime d && d != default ? d.Date : null;
}

