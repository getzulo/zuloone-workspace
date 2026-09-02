using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// The generated entity classes (Currency, PayrollAccrual, …TablePartRow). A test
// script does NOT get this namespace as a global using, so it must be named.
using ZuloOne.Runtime.Generated;

// Соцстрах: проведённое начисление ФОТ порождает начисление взносов.
// Проверяем арифметику (ставки работника/работодателя), потолок базы,
// разные ставки для граждан и иностранцев и необязательность контура.
//
// Всё — типизированными сущностями через менеджеры: справочник это
// NewRecord<T> → заполнить → SaveRecordAsync, документ — NewDocumentAsync<T> →
// заполнить Lines → SaveDocumentAsync, проведение — присваивание подтипа плюс
// сохранение.
public class SocialInsuranceTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private async Task<(Guid Division, Guid Home, Guid Foreign)> SetupAsync()
    {
        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Saudi Riyal";
        currency.Code = "SAR";
        currency.Symbol = "﷼";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var home = DictionaryManager.NewRecord<Country>();
        home.Name = "Saudi Arabia";
        home.CodeISO2 = "SA";
        home.CodeISO3 = "SAU";
        home.PhoneCode = "966";
        home = await DictionaryManager.SaveRecordAsync(home);

        var foreign = DictionaryManager.NewRecord<Country>();
        foreign.Name = "Egypt";
        foreign.CodeISO2 = "EG";
        foreign.CodeISO3 = "EGY";
        foreign.PhoneCode = "20";
        foreign = await DictionaryManager.SaveRecordAsync(foreign);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "ACME KSA";
        legalEntity.RegistrationNumber = "REG-SI-1";
        legalEntity.Country = home.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"HQ-{Db.NewId():N}"[..12];
        divisionType.Name = "Head office";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "HQ";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        return (division.MetaId, home.MetaId, foreign.MetaId);
    }

    /// <summary>Ставки КСА (GOSI): 9.75% работник, 11.75% работодатель, 2% за иностранцев, потолок 45 000.</summary>
    private async Task ConfigureAsync(Guid home, decimal ceiling = 45000m)
    {
        var settings = DictionaryManager.NewRecord<HRSettings>();
        settings.PayrollRunDay = 25;
        settings.WorkHoursPerDay = 8m;
        settings.HomeCountry = home;
        settings.SocialInsuranceEmployeeRate = 0.0975m;
        settings.SocialInsuranceEmployerRate = 0.1175m;
        settings.SocialInsuranceForeignEmployerRate = 0.02m;
        settings.SocialInsuranceWageCeiling = ceiling;
        await DictionaryManager.SaveRecordAsync(settings);
    }

    private async Task<Guid> NewEmployeeAsync(Guid division, Guid nationality, string name)
    {
        var position = DictionaryManager.NewRecord<Position>();
        position.Name = $"Dev-{Db.NewId():N}"[..12];
        position.HourlyRate = 50m;
        position = await DictionaryManager.SaveRecordAsync(position);

        var employee = DictionaryManager.NewRecord<Employee>();
        employee.Name = name;
        employee.Division = division;
        employee.Position = position.MetaId;
        employee.HireDate = new DateTime(2024, 1, 1);
        employee.IsActive = true;
        employee.Nationality = nationality;
        employee = await DictionaryManager.SaveRecordAsync(employee);
        return employee.MetaId;
    }

    // Создаём в исходном подтипе и переводим отдельным шагом: подтип Posted
    // заперт (isReadOnly), строки в него сразу записать нельзя.
    private async Task<PayrollAccrual> AccrueAsync(Guid division, IEnumerable<(Guid Employee, decimal Amount)> lines)
    {
        var accrual = await DocumentManager.NewDocumentAsync<PayrollAccrual>();
        accrual.Division = division;
        foreach (var line in lines)
            accrual.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = line.Employee, Amount = line.Amount });
        await DocumentManager.SaveDocumentAsync(accrual);

        // Черновик взносов не порождает. Проверяем ДО перевода: без этого
        // утверждения ниже проходят и тогда, когда начисление взносов создало
        // сохранение, а не проведение.
        Assert.IsTrue(await DocumentManager.CountDocumentsAsync<SocialInsuranceAccrual>() == 0,
            "черновик ФОТ не должен порождать начисление взносов");

        accrual.Subtype = PayrollAccrual.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(accrual);
        return accrual;
    }

    /// <summary>Единственное начисление взносов вместе с его строками.</summary>
    private async Task<SocialInsuranceAccrual> TheContributionAsync()
    {
        var all = await DocumentManager.QueryDocumentsAsync<SocialInsuranceAccrual>();
        Assert.IsTrue(all.Count == 1, "должно появиться одно начисление взносов, факт {0}", all.Count);
        // Списки документов приходят ЗАГОЛОВКАМИ — строки грузит только чтение
        // одного документа.
        return (await DocumentManager.GetDocumentAsync<SocialInsuranceAccrual>(all[0].MetaId))!;
    }

    [IntegrationTest("Начисление ФОТ порождает взносы по ставкам гражданина")]
    public async Task AccrualCreatesLocalContributions()
    {
        var s = await SetupAsync();
        await ConfigureAsync(s.Home);
        var emp = await NewEmployeeAsync(s.Division, s.Home, "Ahmed");

        // 10 000 × 9.75% = 975 работник; × 11.75% = 1175 работодатель.
        var pa = await AccrueAsync(s.Division, new[] { (emp, 10000m) });

        var si = await TheContributionAsync();
        Assert.IsTrue(si.Lines.Count == 1, "одна строка взносов, факт {0}", si.Lines.Count);
        Assert.IsTrue(si.Lines[0].EmployeeContribution == 975m,
            "взнос работника = 10000 × 9.75% = 975, факт {0}", si.Lines[0].EmployeeContribution);
        Assert.IsTrue(si.Lines[0].EmployerContribution == 1175m,
            "взнос работодателя = 10000 × 11.75% = 1175, факт {0}", si.Lines[0].EmployerContribution);

        // Взносы проведены в регистр, а не остались черновиком.
        decimal regEmployee = 0m, regEmployer = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("SocialInsurance"))
        {
            regEmployee += Convert.ToDecimal(r["EmployeeContribution"]);
            regEmployer += Convert.ToDecimal(r["EmployerContribution"]);
        }
        Assert.IsTrue(regEmployee == 975m, "регистр: взнос работника 975, факт {0}", regEmployee);
        Assert.IsTrue(regEmployer == 1175m, "регистр: взнос работодателя 1175, факт {0}", regEmployer);

        var family = await DocumentManager.GetDocumentFamilyAsync(pa.MetaId);
        Assert.IsTrue(family.Edges.Count > 0, "взносы связаны с начислением ФОТ");
    }

    [IntegrationTest("Удержание взноса уменьшает задолженность перед сотрудником до нетто")]
    public async Task WithholdingReducesLiabilityToNet()
    {
        var s = await SetupAsync();
        await ConfigureAsync(s.Home);
        var emp = await NewEmployeeAsync(s.Division, s.Home, "Fatima");

        // Gross 10000, взнос работника 975 — сотруднику причитается нетто 9025.
        // PayrollAccrualTx признал gross, SocialInsuranceAccrualTx удержал долю
        // работника — регистр обязан отражать итог обеих проводок.
        await AccrueAsync(s.Division, new[] { (emp, 10000m) });
        await TheContributionAsync();

        decimal liability = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("PayrollLiability"))
            liability += Convert.ToDecimal(r["Amount"]);
        Assert.IsTrue(liability == 9025m,
            "задолженность = gross 10000 − удержано 975 = 9025, факт {0}", liability);
    }

    [IntegrationTest("За иностранца платит только работодатель и по своей ставке")]
    public async Task ForeignStaffPaysEmployerRateOnly()
    {
        var s = await SetupAsync();
        await ConfigureAsync(s.Home);
        var emp = await NewEmployeeAsync(s.Division, s.Foreign, "Mahmoud");

        await AccrueAsync(s.Division, new[] { (emp, 10000m) });

        var si = await TheContributionAsync();
        Assert.IsTrue(si.Lines[0].EmployeeContribution == 0m,
            "с иностранца взнос работника не удерживается, факт {0}", si.Lines[0].EmployeeContribution);
        Assert.IsTrue(si.Lines[0].EmployerContribution == 200m,
            "взнос работодателя = 10000 × 2% = 200, факт {0}", si.Lines[0].EmployerContribution);
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

        var si = await TheContributionAsync();
        Assert.IsTrue(si.Lines.Count == 1, "строки одного сотрудника схлопываются в одну, факт {0}", si.Lines.Count);
        Assert.IsTrue(si.Lines[0].ContributoryBase == 45000m,
            "база ограничена потолком 45000, факт {0}", si.Lines[0].ContributoryBase);
        Assert.IsTrue(si.Lines[0].EmployeeContribution == 4387.5m,
            "взнос работника = 45000 × 9.75% = 4387.5, факт {0}", si.Lines[0].EmployeeContribution);
    }

    [IntegrationTest("Без настроек соцстраха начисление ФОТ проводится как раньше")]
    public async Task NoSettingsStillAccrues()
    {
        var s = await SetupAsync();
        // ConfigureAsync НЕ вызываем: настроек соцстраха нет.
        var emp = await NewEmployeeAsync(s.Division, s.Home, "Nadia");

        var pa = await AccrueAsync(s.Division, new[] { (emp, 10000m) });

        var stored = await DocumentManager.GetDocumentAsync<PayrollAccrual>(pa.MetaId);
        Assert.IsTrue(stored?.Subtype == PayrollAccrual.Subtypes.Posted,
            "ФОТ проведён несмотря на ненастроенный соцстрах, факт {0}", stored?.Subtype);
        Assert.IsTrue(await DocumentManager.CountDocumentsAsync<SocialInsuranceAccrual>() == 0,
            "начисление взносов не создано");

        // И сам ФОТ отработал: задолженность перед сотрудником признана.
        decimal liability = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("PayrollLiability"))
            liability += Convert.ToDecimal(r["Amount"]);
        Assert.IsTrue(liability == 10000m, "задолженность 10000, факт {0}", liability);
    }

    [IntegrationTest("Повторное проведение не удваивает взносы")]
    public async Task RepostDoesNotDuplicateContributions()
    {
        var s = await SetupAsync();
        await ConfigureAsync(s.Home);
        var emp = await NewEmployeeAsync(s.Division, s.Home, "Omar");

        var pa = await AccrueAsync(s.Division, new[] { (emp, 10000m) });
        Assert.IsTrue(await DocumentManager.CountDocumentsAsync<SocialInsuranceAccrual>() == 1,
            "после первого проведения ровно одно начисление взносов");

        // Откат и повторное проведение — ровно тот случай, в котором цепочка
        // OnAfterPost проходит по документу второй раз. Без гарда идемпотентности
        // здесь появился бы ВТОРОЙ документ взносов, удвоив и обязательство перед
        // фондом, и удержание у сотрудника.
        var stored = (await DocumentManager.GetDocumentAsync<PayrollAccrual>(pa.MetaId))!;
        stored.Subtype = PayrollAccrual.Subtypes.Draft;
        await DocumentManager.SaveDocumentAsync(stored);

        stored.Subtype = PayrollAccrual.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(stored);

        Assert.IsTrue(await DocumentManager.CountDocumentsAsync<SocialInsuranceAccrual>() == 1,
            "после повторного проведения взносы по-прежнему одни, факт {0}",
            await DocumentManager.CountDocumentsAsync<SocialInsuranceAccrual>());

        // Главное — не количество документов, а суммы: обязательство перед фондом
        // должно остаться одинарным.
        decimal employee = 0m, employer = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("SocialInsurance"))
        {
            employee += Convert.ToDecimal(r["EmployeeContribution"]);
            employer += Convert.ToDecimal(r["EmployerContribution"]);
        }
        Assert.IsTrue(employee == 975m, "взнос работника не удвоился: 975, факт {0}", employee);
        Assert.IsTrue(employer == 1175m, "взнос работодателя не удвоился: 1175, факт {0}", employer);
    }
}
