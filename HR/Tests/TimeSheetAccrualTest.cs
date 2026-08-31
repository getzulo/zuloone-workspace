using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы (TimeSheet, TimeSheetLinesTablePartRow, Employee…).
// Тест-скрипты НЕ получают это пространство имён глобальным using'ом.
using ZuloOne.Runtime.Generated;

// Табель → начисление: команда читает часы, берёт ставку из должности сотрудника
// и порождает проведённое начисление ФОТ. Проверяется именно расчёт по ставке —
// суммы в табеле нет, она появляется только из связки часы × ставка.
//
// Данные строятся менеджерами и типизированными сущностями: справочники через
// IDictionaryManager, табель через IDocumentManager (утверждение — присваивание
// подтипа плюс сохранение), остатки регистров — через ITotalsManager.
public class TimeSheetAccrualTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

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
        legalEntity.RegistrationNumber = "REG-TS-1";
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

        // Две должности с разными ставками — расчёт должен различать сотрудников.
        var senior = DictionaryManager.NewRecord<Position>();
        senior.Name = "Мастер";
        senior.HourlyRate = 50m;
        senior = await DictionaryManager.SaveRecordAsync(senior);

        var junior = DictionaryManager.NewRecord<Position>();
        junior.Name = "Ученик";
        junior.HourlyRate = 25m;
        junior = await DictionaryManager.SaveRecordAsync(junior);

        var emp1 = DictionaryManager.NewRecord<Employee>();
        emp1.Name = "Иванов";
        emp1.Division = division.MetaId;
        emp1.Position = senior.MetaId;
        emp1.HireDate = DateTime.UtcNow.Date;
        emp1.IsActive = true;
        emp1 = await DictionaryManager.SaveRecordAsync(emp1);

        var emp2 = DictionaryManager.NewRecord<Employee>();
        emp2.Name = "Петров";
        emp2.Division = division.MetaId;
        emp2.Position = junior.MetaId;
        emp2.HireDate = DateTime.UtcNow.Date;
        emp2.IsActive = true;
        emp2 = await DictionaryManager.SaveRecordAsync(emp2);

        return (division.MetaId, emp1.MetaId, emp2.MetaId);
    }

    private async Task<decimal> RegisterTotalAsync(string register)
    {
        decimal total = 0m;
        foreach (var row in await TotalsManager.QueryBalancesAsync(register))
            total += Convert.ToDecimal(row["Amount"]);
        return total;
    }

    // Табель проходит СВОЙ маршрут: создаётся в начальном подтипе Draft и
    // утверждается переходом, а не рождается сразу утверждённым.
    private async Task<TimeSheet> ApprovedSheetAsync(Guid division, params (Guid Employee, decimal Hours)[] lines)
    {
        var sheet = await DocumentManager.NewDocumentAsync<TimeSheet>();
        sheet.Division = division;
        foreach (var line in lines)
            sheet.Lines.Add(new TimeSheetLinesTablePartRow { Employee = line.Employee, Hours = line.Hours });
        await DocumentManager.SaveDocumentAsync(sheet);

        sheet.Subtype = TimeSheet.Subtypes.Approved;
        await DocumentManager.SaveDocumentAsync(sheet);
        return sheet;
    }

    [IntegrationTest("Команда начисляет ФОТ из часов по ставке должности")]
    public async Task AccruesFromHours()
    {
        var s = await SetupAsync();
        var sheet = await ApprovedSheetAsync(s.Division, (s.Emp1, 10m), (s.Emp2, 8m));

        // Утверждённый табель — это факт работы, а не деньги: пока команда не
        // отработала, регистры ФОТ пусты. Без этого снимка проверки ниже проходят
        // даже когда команда ничего не сделала.
        Assert.IsTrue(await RegisterTotalAsync("Payroll") == 0m, "сам табель ФОТ не начисляет");

        // Исполнение команд — единственный шаг сценария БЕЗ менеджера: платформа
        // не публикует ICommandManager, запускать команду умеет только харнесс.
        var commandId = await Db.FindCommandIdAsync("document", "AccruePayroll");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, sheet.MetaId);
        Assert.IsTrue(run.Success, "команда должна выполниться: {0}", run.Message ?? "");

        // 10×50 + 8×25 = 500 + 200 = 700 — суммы нет в табеле, она посчитана по ставкам.
        var payroll = await RegisterTotalAsync("Payroll");
        Assert.IsTrue(payroll == 700m, "ФОТ = 10×50 + 8×25 = 700, факт {0}", payroll);

        var liab = await RegisterTotalAsync("PayrollLiability");
        Assert.IsTrue(liab == 700m, "задолженность перед сотрудниками 700, факт {0}", liab);

        Assert.IsTrue(string.Join("; ", run.ClientMessages).Contains("700"),
            "пользователь видит итог начисления: {0}", string.Join("; ", run.ClientMessages));
    }

    [IntegrationTest("Пустой табель ничего не начисляет")]
    public async Task EmptySheetAccruesNothing()
    {
        var s = await SetupAsync();
        var sheet = await ApprovedSheetAsync(s.Division);

        var commandId = await Db.FindCommandIdAsync("document", "AccruePayroll");
        var run = await Db.ExecuteDocumentCommandAsync(commandId, sheet.MetaId);

        var payroll = await RegisterTotalAsync("Payroll");
        Assert.IsTrue(payroll == 0m, "по пустому табелю начислений нет, факт {0}", payroll);
        Assert.IsTrue(string.Join("; ", run.ClientMessages).Contains("нет строк"),
            "пользователь получил причину: {0}", string.Join("; ", run.ClientMessages));
    }
}
