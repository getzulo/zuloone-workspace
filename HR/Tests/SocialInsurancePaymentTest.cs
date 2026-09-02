using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы сущностей (SocialInsurancePayment, HRSettings…).
// Тестовые скрипты НЕ получают это пространство имён глобальным using.
using ZuloOne.Runtime.Generated;

// Замыкание контура соцстраха: до появления SocialInsurancePayment регистр
// SocialInsurance умел только расти — взносы начислялись и не гасились ничем,
// ровно как кредиторка до VendorPayment и задолженность по ФОТ до GL-ноги выплаты.
//
// Цепочка гоняется целиком и по-настоящему: начисление ФОТ → порождённое им
// начисление взносов → платёж в фонд. Прямых движений в регистр нет нигде —
// иначе тест доказывал бы работу регистра, а не бизнес-цепочки.
public class SocialInsurancePaymentTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private async Task<(Guid Division, Guid Home)> SetupAsync()
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

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "ACME KSA";
        legalEntity.RegistrationNumber = $"REG-SIP-{Db.NewId():N}"[..16];
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

        var settings = DictionaryManager.NewRecord<HRSettings>();
        settings.PayrollRunDay = 25;
        settings.WorkHoursPerDay = 8m;
        settings.HomeCountry = home.MetaId;
        settings.SocialInsuranceEmployeeRate = 0.0975m;
        settings.SocialInsuranceEmployerRate = 0.1175m;
        settings.SocialInsuranceForeignEmployerRate = 0.02m;
        settings.SocialInsuranceWageCeiling = 45000m;
        await DictionaryManager.SaveRecordAsync(settings);

        return (division.MetaId, home.MetaId);
    }

    private async Task<Guid> NewEmployeeAsync(Guid division, Guid nationality)
    {
        var position = DictionaryManager.NewRecord<Position>();
        position.Name = $"Dev-{Db.NewId():N}"[..12];
        position.HourlyRate = 50m;
        position = await DictionaryManager.SaveRecordAsync(position);

        var employee = DictionaryManager.NewRecord<Employee>();
        employee.Name = "Ahmed";
        employee.Division = division;
        employee.Position = position.MetaId;
        employee.HireDate = new DateTime(2024, 1, 1);
        employee.IsActive = true;
        employee.Nationality = nationality;
        employee = await DictionaryManager.SaveRecordAsync(employee);
        return employee.MetaId;
    }

    /// <summary>Начисление ФОТ, проведённое по-настоящему: оно и порождает взносы.</summary>
    private async Task<SocialInsuranceAccrual> AccrueAsync(Guid division, Guid employee, decimal gross)
    {
        var accrual = await DocumentManager.NewDocumentAsync<PayrollAccrual>();
        accrual.Division = division;
        accrual.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = employee, Amount = gross });
        await DocumentManager.SaveDocumentAsync(accrual);

        accrual.Subtype = PayrollAccrual.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(accrual);

        var all = await DocumentManager.QueryDocumentsAsync<SocialInsuranceAccrual>();
        Assert.IsTrue(all.Count == 1, "должно появиться одно начисление взносов, факт {0}", all.Count);
        return (await DocumentManager.GetDocumentAsync<SocialInsuranceAccrual>(all[0].MetaId))!;
    }

    /// <summary>Обязательство перед фондом: обе доли взноса по всем строкам баланса.</summary>
    private static async Task<(decimal Employee, decimal Employer)> FundAsync()
    {
        decimal employee = 0m, employer = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("SocialInsurance"))
        {
            employee += Convert.ToDecimal(r["EmployeeContribution"]);
            employer += Convert.ToDecimal(r["EmployerContribution"]);
        }
        return (employee, employer);
    }

    [IntegrationTest("Платёж в фонд закрывает обязательство по обеим долям взноса")]
    public async Task PaymentClearsFundLiability()
    {
        var s = await SetupAsync();
        var emp = await NewEmployeeAsync(s.Division, s.Home);

        // 10 000 × 9.75% = 975 работник; × 11.75% = 1175 работодатель.
        var si = await AccrueAsync(s.Division, emp, 10000m);

        var accrued = await FundAsync();
        Assert.IsTrue(accrued.Employee == 975m && accrued.Employer == 1175m,
            "начислено 975/1175, факт {0}/{1}", accrued.Employee, accrued.Employer);

        var payment = await DocumentManager.NewDocumentAsync<SocialInsurancePayment>();
        payment.Division = s.Division;
        payment.Lines.Add(new SocialInsurancePaymentLinesTablePartRow
        {
            Employee = emp,
            EmployeeContribution = si.Lines[0].EmployeeContribution,
            EmployerContribution = si.Lines[0].EmployerContribution,
        });
        await DocumentManager.SaveDocumentAsync(payment);

        // Черновик платежа ничего не гасит: движения принадлежат подтипу Paid.
        var stillOwed = await FundAsync();
        Assert.IsTrue(stillOwed.Employee == 975m && stillOwed.Employer == 1175m,
            "черновик платежа не гасит обязательство, факт {0}/{1}", stillOwed.Employee, stillOwed.Employer);

        payment.Subtype = SocialInsurancePayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(payment);

        var settled = await FundAsync();
        Assert.IsTrue(settled.Employee == 0m && settled.Employer == 0m,
            "после платежа обязательство перед фондом 0/0, факт {0}/{1}", settled.Employee, settled.Employer);

        // Платёж в фонд не должен возвращать сотруднику удержанное: начисление
        // взносов уменьшило задолженность по ФОТ, и это уменьшение остаётся.
        //
        // Сумма по ВСЕМУ регистру, а не срез по сотруднику: у PayrollLiability нет
        // физических измерений, Employee там динамическая аналитика, и ключ в
        // GetBalanceAsync по ней молча игнорируется — вернулся бы тот же итог, но
        // с видом персонального среза. В кейсе сотрудник один, поэтому итог и есть
        // его остаток.
        decimal payroll = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("PayrollLiability"))
            payroll += Convert.ToDecimal(r["Amount"]);
        Assert.IsTrue(payroll == 10000m - 975m,
            "задолженность по ФОТ остаётся нетто 9025, факт {0}", payroll);
    }

    [IntegrationTest("Частичный платёж гасит ровно свою часть")]
    public async Task PartialPaymentSettlesPartially()
    {
        var s = await SetupAsync();
        var emp = await NewEmployeeAsync(s.Division, s.Home);
        await AccrueAsync(s.Division, emp, 10000m);

        // Платим только долю работника — доля работодателя остаётся висеть.
        var payment = await DocumentManager.NewDocumentAsync<SocialInsurancePayment>();
        payment.Division = s.Division;
        payment.Lines.Add(new SocialInsurancePaymentLinesTablePartRow
        {
            Employee = emp,
            EmployeeContribution = 975m,
            EmployerContribution = 0m,
        });
        await DocumentManager.SaveDocumentAsync(payment);
        payment.Subtype = SocialInsurancePayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(payment);

        var rest = await FundAsync();
        Assert.IsTrue(rest.Employee == 0m, "доля работника погашена, факт {0}", rest.Employee);
        Assert.IsTrue(rest.Employer == 1175m, "доля работодателя ещё висит, факт {0}", rest.Employer);
    }

    [IntegrationTest("Платёж без строк отклоняется")]
    public async Task EmptyPaymentRejected()
    {
        var s = await SetupAsync();
        var emp = await NewEmployeeAsync(s.Division, s.Home);
        await AccrueAsync(s.Division, emp, 10000m);

        var payment = await DocumentManager.NewDocumentAsync<SocialInsurancePayment>();
        payment.Division = s.Division;
        await DocumentManager.SaveDocumentAsync(payment);

        // Обработчик отказывает исключением, а бросок обрекает окружающую
        // транзакцию прогона — поэтому после catch к базе больше не обращаемся.
        var rejected = false;
        try
        {
            payment.Subtype = SocialInsurancePayment.Subtypes.Paid;
            await DocumentManager.SaveDocumentAsync(payment);
        }
        catch (Exception ex) when (ex.Message.Contains("строки"))
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "пустой платёж обязан быть отклонён с внятной причиной");
    }

    [IntegrationTest("Платёж с нулевой суммой отклоняется")]
    public async Task ZeroPaymentRejected()
    {
        var s = await SetupAsync();
        var emp = await NewEmployeeAsync(s.Division, s.Home);
        await AccrueAsync(s.Division, emp, 10000m);

        var payment = await DocumentManager.NewDocumentAsync<SocialInsurancePayment>();
        payment.Division = s.Division;
        payment.Lines.Add(new SocialInsurancePaymentLinesTablePartRow
        {
            Employee = emp,
            EmployeeContribution = 0m,
            EmployerContribution = 0m,
        });
        await DocumentManager.SaveDocumentAsync(payment);

        var rejected = false;
        try
        {
            payment.Subtype = SocialInsurancePayment.Subtypes.Paid;
            await DocumentManager.SaveDocumentAsync(payment);
        }
        catch (Exception ex) when (ex.Message.Contains("больше нуля"))
        {
            rejected = true;
        }

        Assert.IsTrue(rejected, "нулевой платёж обязан быть отклонён с внятной причиной");
    }
}
