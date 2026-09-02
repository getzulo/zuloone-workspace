using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы сущностей (PayrollPayment, ChartOfAccounts, AccountType…).
// Тестовые скрипты НЕ получают это пространство имён глобальным using.
using ZuloOne.Runtime.Generated;

// Замыкание ФОТ в главной книге. Начисление кредитует счёт задолженности перед
// сотрудниками, а дебетовать его было нечем: в регистре PayrollLiability долг
// гасился выплатой, а в книге рос бесконечно — расхождение регистра и книги.
//
// Тест гоняет ОБЕ половины на одних и тех же данных и проверяет главное: после
// начисления и выплаты счёт задолженности в GL нетто-ноль, ровно как остаток
// регистра. Проверка идёт ПО СЧЁТУ, а не суммой по всей книге — в сумме дебет и
// кредит сходятся всегда (это инвариант двойной записи), и такая проверка
// прошла бы, даже если бы выплата дебетовала не тот счёт.
public class PayrollPaymentGLTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
    private static ITotalsManager TotalsManager => GetService<ITotalsManager>();

    private sealed class Setup
    {
        public Guid Employee;
        public Guid Division;
        public Guid LiabilityAccount;
        public Guid CashAccount;
        public Guid ExpenseAccount;
    }

    private async Task<Setup> SetupAsync()
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
        legalEntity.RegistrationNumber = $"REG-PPG-{Db.NewId():N}"[..16];
        legalEntity.Country = country.MetaId;
        legalEntity.Currency = currency.MetaId;
        legalEntity = await DictionaryManager.SaveRecordAsync(legalEntity);

        var divisionType = DictionaryManager.NewRecord<DivisionType>();
        divisionType.Code = "HR";
        divisionType.Name = "Staff";
        divisionType = await DictionaryManager.SaveRecordAsync(divisionType);

        var division = DictionaryManager.NewRecord<Division>();
        division.Name = "Main";
        division.LegalEntity = legalEntity.MetaId;
        division.DivisionType = divisionType.MetaId;
        division = await DictionaryManager.SaveRecordAsync(division);

        var position = DictionaryManager.NewRecord<Position>();
        position.Name = "Operator";
        position.HourlyRate = 50m;
        position = await DictionaryManager.SaveRecordAsync(position);

        var employee = DictionaryManager.NewRecord<Employee>();
        employee.Name = "Hans Muster";
        employee.Division = division.MetaId;
        employee.Position = position.MetaId;
        employee.HireDate = today;
        employee.IsActive = true;
        employee = await DictionaryManager.SaveRecordAsync(employee);

        var expenseAccount = DictionaryManager.NewRecord<ChartOfAccounts>();
        expenseAccount.Code = "7000";
        expenseAccount.Name = "Payroll expense";
        expenseAccount.AccountType = AccountType.Expense;
        expenseAccount.IsPostable = true;
        expenseAccount.Currency = currency.MetaId;
        expenseAccount = await DictionaryManager.SaveRecordAsync(expenseAccount);

        var liabilityAccount = DictionaryManager.NewRecord<ChartOfAccounts>();
        liabilityAccount.Code = "2100";
        liabilityAccount.Name = "Payroll liability";
        liabilityAccount.AccountType = AccountType.Liability;
        liabilityAccount.IsPostable = true;
        liabilityAccount.Currency = currency.MetaId;
        liabilityAccount = await DictionaryManager.SaveRecordAsync(liabilityAccount);

        // Счёт денежных средств — кредитовая сторона выплаты. Без него выплата
        // не имела бы во что разнестись, и книга осталась бы незакрытой.
        var cashAccount = DictionaryManager.NewRecord<ChartOfAccounts>();
        cashAccount.Code = "1000";
        cashAccount.Name = "Cash";
        cashAccount.AccountType = AccountType.Asset;
        cashAccount.IsPostable = true;
        cashAccount.Currency = currency.MetaId;
        cashAccount = await DictionaryManager.SaveRecordAsync(cashAccount);

        var settings = DictionaryManager.NewRecord<AccountingSettings>();
        settings.ArAccountCode = "1200";
        settings.RevenueAccountCode = "4000";
        settings.InventoryAccountCode = "1400";
        settings.PayableAccountCode = "2000";
        settings.PayrollExpenseAccountCode = "7000";
        settings.PayrollLiabilityAccountCode = "2100";
        settings.CashAccountCode = "1000";
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

        return new Setup
        {
            Employee = employee.MetaId,
            Division = division.MetaId,
            LiabilityAccount = liabilityAccount.MetaId,
            CashAccount = cashAccount.MetaId,
            ExpenseAccount = expenseAccount.MetaId,
        };
    }

    /// <summary>
    /// Дебет/кредит ОДНОГО счёта по проводкам, порождённым документом.
    /// Через строки JournalEntry, а не через регистр GL: у GL нет физических
    /// измерений, его таблица остатков схлопывает всё в одну строку, а движения
    /// несут только ссылку на набор аналитик — разрез по счёту оттуда не достать.
    /// Строка проводки несёт Account типизированным полем, и это ровно тот факт,
    /// который проверяется: КАКОЙ счёт задет, а не только «дебет сошёлся с кредитом».
    /// </summary>
    private static async Task<(decimal Debit, decimal Credit)> AccountAsync(Guid document, Guid account)
    {
        decimal debit = 0m, credit = 0m;

        var family = await DocumentManager.GetDocumentFamilyAsync(document);
        var children = family.Edges.Where(e => e.ParentDocId == document).Select(e => e.ChildDocId).Distinct();

        foreach (var childId in children)
        {
            var entry = await DocumentManager.GetDocumentAsync<JournalEntry>(childId);
            if (entry == null) continue;

            foreach (var line in entry.Lines.Where(l => l.Account == account))
            {
                debit += line.Debit;
                credit += line.Credit;
            }
        }

        return (debit, credit);
    }

    private static async Task<decimal> LiabilityRegisterAsync(Guid employee)
        => await TotalsManager.GetBalanceAsync("PayrollLiability", "Amount",
            new Dictionary<string, object?> { ["Employee"] = employee });

    [IntegrationTest("Выплата ФОТ дебетует задолженность и кредитует денежные средства")]
    public async Task PaymentPostsToLedger()
    {
        var s = await SetupAsync();

        var accrual = await DocumentManager.NewDocumentAsync<PayrollAccrual>();
        accrual.Division = s.Division;
        accrual.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = s.Employee, Amount = 700m });
        await DocumentManager.SaveDocumentAsync(accrual);
        accrual.Subtype = PayrollAccrual.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(accrual);

        // После начисления долг признан обеими сторонами: и в книге, и в регистре.
        var afterAccrual = await AccountAsync(accrual.MetaId, s.LiabilityAccount);
        Assert.IsTrue(afterAccrual.Credit == 700m,
            "начисление кредитует счёт задолженности на 700, факт {0}", afterAccrual.Credit);
        Assert.IsTrue(afterAccrual.Debit == 0m,
            "начисление счёт задолженности не дебетует, факт {0}", afterAccrual.Debit);

        var payment = await DocumentManager.NewDocumentAsync<PayrollPayment>();
        payment.Lines.Add(new PayrollPaymentLinesTablePartRow { Employee = s.Employee, Amount = 700m });
        await DocumentManager.SaveDocumentAsync(payment);

        // Черновик выплаты в книгу не попадает — без этой проверки утверждения
        // ниже прошли бы и в случае, если выплата разносится сама при сохранении.
        Assert.IsTrue((await AccountAsync(payment.MetaId, s.LiabilityAccount)).Debit == 0m,
            "черновик выплаты не должен дебетовать задолженность");

        payment.Subtype = PayrollPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(payment);

        // Главное: выплата дебетует ИМЕННО счёт задолженности, закрывая то, что
        // начисление на нём кредитовало.
        var liability = await AccountAsync(payment.MetaId, s.LiabilityAccount);
        Assert.IsTrue(liability.Debit == 700m,
            "выплата дебетует счёт задолженности на 700, факт {0}", liability.Debit);

        var cash = await AccountAsync(payment.MetaId, s.CashAccount);
        Assert.IsTrue(cash.Credit == 700m,
            "выплата кредитует денежные средства на 700, факт {0}", cash.Credit);

        // Счёт задолженности в книге нетто-ноль: начислено 700 кредитом, выплачено
        // 700 дебетом — ровно то расхождение с регистром, ради которого всё делалось.
        var netLiability = afterAccrual.Credit - liability.Debit;
        Assert.IsTrue(netLiability == 0m,
            "после выплаты счёт задолженности нетто-ноль, факт {0}", netLiability);

        // И регистр HR говорит то же самое.
        var register = await LiabilityRegisterAsync(s.Employee);
        Assert.IsTrue(register == 0m, "регистр PayrollLiability обнулён, факт {0}", register);
    }

    [IntegrationTest("Частичная выплата гасит долг в книге ровно на свою сумму")]
    public async Task PartialPaymentPostsPartially()
    {
        var s = await SetupAsync();

        var accrual = await DocumentManager.NewDocumentAsync<PayrollAccrual>();
        accrual.Division = s.Division;
        accrual.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = s.Employee, Amount = 700m });
        await DocumentManager.SaveDocumentAsync(accrual);
        accrual.Subtype = PayrollAccrual.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(accrual);

        var payment = await DocumentManager.NewDocumentAsync<PayrollPayment>();
        payment.Lines.Add(new PayrollPaymentLinesTablePartRow { Employee = s.Employee, Amount = 300m });
        await DocumentManager.SaveDocumentAsync(payment);
        payment.Subtype = PayrollPayment.Subtypes.Paid;
        await DocumentManager.SaveDocumentAsync(payment);

        var liability = await AccountAsync(payment.MetaId, s.LiabilityAccount);
        Assert.IsTrue(liability.Debit == 300m,
            "частичная выплата дебетует 300, факт {0}", liability.Debit);

        var cash = await AccountAsync(payment.MetaId, s.CashAccount);
        Assert.IsTrue(cash.Credit == 300m,
            "частичная выплата кредитует денежные средства на 300, факт {0}", cash.Credit);

        // Книга и регистр расходиться не должны и на промежуточной сумме:
        // начислено 700 − выплачено 300 = 400 непогашенного долга в обоих.
        var register = await LiabilityRegisterAsync(s.Employee);
        Assert.IsTrue(register == 400m, "регистр PayrollLiability = 400, факт {0}", register);
    }
}
