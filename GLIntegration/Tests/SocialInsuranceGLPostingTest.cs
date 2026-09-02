using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы сущностей. Тестовым скриптам это пространство имён НЕ
// приходит глобальным using'ом.
using ZuloOne.Runtime.Generated;

// ВЗНОСЫ НА СОЦСТРАХ В ГЛАВНОЙ КНИГЕ.
//
// Начисление ФОТ порождает начисление взносов, а оно — ДВЕ собственные ноги GL
// поверх уже существующей ноги самого ФОТ: реклассификация удержанной доли
// (Dr задолженность перед сотрудниками / Cr задолженность перед фондом) и
// расход работодателя (Dr расход на соцстрах / Cr задолженность перед фондом).
// Пятый потребитель GeneralLedgerService после ФОТ, продаж, закупок и
// себестоимости.
//
// Все три пары проводок срабатывают одной цепочкой внутри одного
// SaveDocumentAsync (начисление ФОТ → синхронное создание и проведение
// начисления взносов), поэтому тест меряет суммарный дебет/кредит регистра, а
// не какую-то пару отдельно — так же, как это уже делают PayrollGLPostingTest
// и CostOfSalesGLTest.
public class SocialInsuranceGLPostingTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    /// <summary>Дебет и кредит по всему регистру GL: разрезы там ДИНАМИЧЕСКИЕ
    /// (Account/LegalEntity/FiscalPeriod), точечный срез не адресуется.</summary>
    private static async Task<(decimal Debit, decimal Credit)> LedgerAsync()
    {
        decimal debit = 0m, credit = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("GL"))
        {
            debit += Convert.ToDecimal(r["Debit"]);
            credit += Convert.ToDecimal(r["Credit"]);
        }
        return (debit, credit);
    }

    [IntegrationTest("Взносы на соцстрах разносятся в GL: удержание и расход работодателя двумя ногами")]
    public async Task ContributionsPostToLedger()
    {
        var today = DateTime.UtcNow.Date;

        var currency = DictionaryManager.NewRecord<Currency>();
        currency.Name = "Saudi Riyal";
        currency.Code = "SAR";
        currency.Symbol = "﷼";
        currency = await DictionaryManager.SaveRecordAsync(currency);

        var country = DictionaryManager.NewRecord<Country>();
        country.Name = "Saudi Arabia";
        country.CodeISO2 = "SA";
        country.CodeISO3 = "SAU";
        country.PhoneCode = "966";
        country = await DictionaryManager.SaveRecordAsync(country);

        var legalEntity = DictionaryManager.NewRecord<LegalEntity>();
        legalEntity.Name = "ACME KSA";
        legalEntity.RegistrationNumber = "REG-SIGL-1";
        legalEntity.Country = country.MetaId;
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

        var position = DictionaryManager.NewRecord<Position>();
        position.Name = "Dev";
        position.HourlyRate = 50m;
        position = await DictionaryManager.SaveRecordAsync(position);

        var employee = DictionaryManager.NewRecord<Employee>();
        employee.Name = "Ahmed";
        employee.Division = division.MetaId;
        employee.Position = position.MetaId;
        employee.HireDate = today;
        employee.IsActive = true;
        employee.Nationality = country.MetaId;
        employee = await DictionaryManager.SaveRecordAsync(employee);

        // Ставки КСА (GOSI): 9.75% работник, 11.75% работодатель — те же, что в
        // HR/Tests/SocialInsuranceTest.cs, чтобы числа были узнаваемы.
        var hrSettings = DictionaryManager.NewRecord<HRSettings>();
        hrSettings.PayrollRunDay = 25;
        hrSettings.WorkHoursPerDay = 8m;
        hrSettings.HomeCountry = country.MetaId;
        hrSettings.SocialInsuranceEmployeeRate = 0.0975m;
        hrSettings.SocialInsuranceEmployerRate = 0.1175m;
        hrSettings.SocialInsuranceForeignEmployerRate = 0.02m;
        hrSettings.SocialInsuranceWageCeiling = 45000m;
        await DictionaryManager.SaveRecordAsync(hrSettings);

        // Счета профиля: пара ФОТ (расход/задолженность) плюс новая пара для
        // соцстраха (задолженность перед фондом, расход работодателя).
        await NewAccountAsync("7000", "Payroll expense", AccountType.Expense, currency.MetaId);
        await NewAccountAsync("2100", "Payroll liability", AccountType.Liability, currency.MetaId);
        await NewAccountAsync("7100", "Social insurance expense", AccountType.Expense, currency.MetaId);
        await NewAccountAsync("2200", "Social insurance payable", AccountType.Liability, currency.MetaId);

        var settings = DictionaryManager.NewRecord<AccountingSettings>();
        settings.ArAccountCode = "1200";
        settings.RevenueAccountCode = "4000";
        settings.InventoryAccountCode = "1400";
        settings.PayableAccountCode = "2000";
        settings.PayrollExpenseAccountCode = "7000";
        settings.PayrollLiabilityAccountCode = "2100";
        settings.SocialInsuranceExpenseAccountCode = "7100";
        settings.SocialInsurancePayableAccountCode = "2200";
        await DictionaryManager.SaveRecordAsync(settings);

        var fiscalYear = DictionaryManager.NewRecord<FiscalYear>();
        fiscalYear.Code = "FY";
        fiscalYear.StartDate = today.AddMonths(-6);
        fiscalYear.EndDate = today.AddMonths(6);
        fiscalYear.IsClosed = false;
        fiscalYear = await DictionaryManager.SaveRecordAsync(fiscalYear);

        var fiscalPeriod = DictionaryManager.NewRecord<FiscalPeriod>();
        fiscalPeriod.Code = "P1";
        fiscalPeriod.FiscalYear = fiscalYear.MetaId;
        fiscalPeriod.FromDate = today.AddDays(-15);
        fiscalPeriod.ToDate = today.AddDays(15);
        fiscalPeriod.Status = "Open";
        await DictionaryManager.SaveRecordAsync(fiscalPeriod);

        var (debit0, credit0) = await LedgerAsync();

        var accrual = await DocumentManager.NewDocumentAsync<PayrollAccrual>();
        accrual.Division = division.MetaId;
        accrual.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = employee.MetaId, Amount = 10000m });
        await DocumentManager.SaveDocumentAsync(accrual);

        accrual.Subtype = PayrollAccrual.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(accrual);

        // Начисление взносов создано и проведено синхронно вместе с ФОТ.
        var contributions = await DocumentManager.QueryDocumentsAsync<SocialInsuranceAccrual>();
        Assert.IsTrue(contributions.Count == 1, "начисление взносов создано, факт {0}", contributions.Count);

        // Три пары проводок одной цепочкой: gross 10000 (начисление ФОТ) +
        // удержано 975 (реклассификация) + 1175 (расход работодателя) = 12150.
        var (debit, credit) = await LedgerAsync();
        var dr = debit - debit0;
        var cr = credit - credit0;
        Assert.IsTrue(dr == 12150m,
            "дебет GL = gross 10000 + удержано 975 + работодатель 1175 = 12150, факт {0}", dr);
        Assert.IsTrue(cr == 12150m,
            "кредит GL = 12150, факт {0}", cr);
        Assert.IsTrue(dr == cr, "проводки сбалансированы: дебет {0} = кредит {1}", dr, cr);

        // Проводки GL должны быть привязаны к начислению взносов — родословная документов.
        var family = await DocumentManager.GetDocumentFamilyAsync(contributions[0].MetaId);
        Assert.IsTrue(family.Edges.Count > 0, "проводки GL связаны с начислением взносов");
    }

    private static async Task NewAccountAsync(string code, string name, AccountType type, Guid currency)
    {
        var account = DictionaryManager.NewRecord<ChartOfAccounts>();
        account.Code = code;
        account.Name = name;
        account.AccountType = type;
        account.IsPostable = true;
        account.Currency = currency;
        await DictionaryManager.SaveRecordAsync(account);
    }
}
