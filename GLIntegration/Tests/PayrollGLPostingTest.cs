using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// The generated entity classes (Currency, PayrollAccrual, AccountType…). A test
// script does NOT get this namespace as a global using, so it must be named —
// without it `Currency` binds to an inaccessible type elsewhere and the rest are
// simply not found.
using ZuloOne.Runtime.Generated;

// Разноска начисления ФОТ в главную книгу: Dr расход на оплату труда /
// Cr задолженность перед сотрудниками. Третий потребитель GeneralLedgerService
// после продаж и закупок — проверяем, что механика повторяется на подсистеме
// без склада и контрагента, где юрлицо берётся через подразделение.
//
// Написан так же, как пишется бизнес-сервис: типизированные сущности через
// менеджеры. Проведение — присваивание подтипа плюс сохранение, а не вызов по
// имени: SaveDocumentAsync сам проводит изменение через движок разноски.
public class PayrollGLPostingTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    [IntegrationTest("Начисление ФОТ разносится в GL: расход = задолженность")]
    public async Task AccrualPostsToLedger()
    {
        var today = DateTime.UtcNow.Date;

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
        legalEntity.RegistrationNumber = "REG-PGL-1";
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = $"OPS-{Db.NewId():N}"[..12];
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

        var employee = DictionaryManager.NewRecord<Employee>();
        employee.Name = "Иванов";
        employee.Division = division.MetaId;
        employee.Position = position.MetaId;
        employee.HireDate = today;
        employee.IsActive = true;
        employee = await DictionaryManager.SaveRecordAsync(employee);

        // Счета профиля: расход на оплату труда и задолженность перед сотрудниками.
        // AccountType — сгенерированное перечисление, а не число и не строка.
        var expenseAccount = DictionaryManager.NewRecord<ChartOfAccounts>();
        expenseAccount.Code = "7000";
        expenseAccount.Name = "Payroll expense";
        expenseAccount.AccountType = AccountType.Expense;
        expenseAccount.IsPostable = true;
        expenseAccount.Currency = currency.MetaId;
        await DictionaryManager.SaveRecordAsync(expenseAccount);

        var liabilityAccount = DictionaryManager.NewRecord<ChartOfAccounts>();
        liabilityAccount.Code = "2100";
        liabilityAccount.Name = "Payroll liability";
        liabilityAccount.AccountType = AccountType.Liability;
        liabilityAccount.IsPostable = true;
        liabilityAccount.Currency = currency.MetaId;
        await DictionaryManager.SaveRecordAsync(liabilityAccount);

        var settings = DictionaryManager.NewRecord<AccountingSettings>();
        settings.ArAccountCode = "1200";
        settings.RevenueAccountCode = "4000";
        settings.InventoryAccountCode = "1400";
        settings.PayableAccountCode = "2000";
        settings.PayrollExpenseAccountCode = "7000";
        settings.PayrollLiabilityAccountCode = "2100";
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

        // Подтип не передаём: документ заводится в НАЧАЛЬНОМ подтипе своего типа.
        var accrual = await DocumentManager.NewDocumentAsync<PayrollAccrual>();
        accrual.Division = division.MetaId;
        accrual.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = employee.MetaId, Amount = 700m });
        await DocumentManager.SaveDocumentAsync(accrual);

        // Черновик в книгу не попадает. Проверяем ДО перевода — без этого
        // утверждения ниже проходят и тогда, когда документ разнёсся сам при
        // сохранении, и тест ничего не доказывает о самом переходе.
        Assert.IsTrue((await TotalsManager.QueryBalancesAsync("GL")).Count == 0,
            "черновик начисления не должен порождать остатков GL");

        accrual.Subtype = PayrollAccrual.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(accrual);

        // GL несёт динамические аналитики — баланс схлопывается, поэтому суммируем.
        decimal debit = 0m, credit = 0m;
        foreach (var r in await TotalsManager.QueryBalancesAsync("GL"))
        {
            debit += Convert.ToDecimal(r["Debit"]);
            credit += Convert.ToDecimal(r["Credit"]);
        }
        Assert.IsTrue(debit == 700m, "дебет GL = 700 (расход на оплату труда), факт {0}", debit);
        Assert.IsTrue(credit == 700m, "кредит GL = 700 (задолженность перед сотрудниками), факт {0}", credit);

        // Проводка должна быть привязана к начислению — родословная документов.
        var family = await DocumentManager.GetDocumentFamilyAsync(accrual.MetaId);
        Assert.IsTrue(family.Edges.Count > 0, "начисление связано с порождённой проводкой ГК");
    }
}
