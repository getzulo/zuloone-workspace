using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
using ZuloOne.Managers;
// Сгенерированные классы сущностей. Тестовым скриптам это пространство имён
// глобальным using'ом не приходит.
using ZuloOne.Runtime.Generated;

// ДАТА ПРОВОДКИ — ДАТА ДОКУМЕНТА, А НЕ ДЕНЬ РАЗНОСКИ.
//
// Разноска в главную книгу вызывается из события after-post, то есть в момент,
// когда пользователь нажал «Провести». Взять там текущую дату кажется
// естественным — и это ошибка, которая проявляется только на документах,
// проводимых задним числом: накладная за август, проведённая второго сентября,
// попадает в сентябрьскую книгу.
//
// Последствие не косметическое. GeneralLedgerService по дате ВЫБИРАЕТ УЧЁТНЫЙ
// ПЕРИОД (ResolvePeriodAsync) и кладёт его в проводку. Регистры при этом
// движутся датой документа — то есть подсистема учёта говорит «август», а
// книга «сентябрь», и сверка регистра с книгой за период расходится ровно на
// сумму всех документов, проведённых с опозданием. Закрытый август уже не
// исправить: проводка легла в другой период.
//
// Тест заводит ДВА непересекающихся периода — прошлый и текущий — и проводит
// начисление, датированное прошлым. Проверяется и дата проводки, и период:
// одной даты мало, период это то, по чему собирается отчётность.
public class PostingDateGLTest : IntegrationTestScriptBase
{
    private static IDictionaryManager DictionaryManager => GetService<IDictionaryManager>();
    private static IDocumentManager DocumentManager => GetService<IDocumentManager>();

    private static readonly DateTime Today = new DateTime(2026, 9, 2);
    private static readonly DateTime Backdated = new DateTime(2026, 7, 15);

    private sealed class Setup
    {
        public Guid Division;
        public Guid PastPeriod;
        public Guid CurrentPeriod;
    }

    private async Task<Setup> SetupAsync()
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
        legalEntity.RegistrationNumber = $"REG-PDT-{Db.NewId():N}"[..16];
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
        employee.HireDate = Backdated;
        employee.IsActive = true;
        employee = await DictionaryManager.SaveRecordAsync(employee);
        Employee = employee.MetaId;

        await AccountAsync("7000", "Payroll expense", AccountType.Expense, currency.MetaId);
        await AccountAsync("2100", "Payroll liability", AccountType.Liability, currency.MetaId);

        // Настройки учёта — ОДИНОЧНЫЙ и КЭШИРУЕМЫЙ справочник: кэш переживает
        // откат кейса, поэтому правим существующую запись, а не заводим слепо.
        var accRows = await DictionaryManager.GetRecordsAsync<AccountingSettings>(null, 1);
        var settings = accRows.Count > 0 ? accRows[0] : DictionaryManager.NewRecord<AccountingSettings>();
        settings.PayrollExpenseAccountCode = "7000";
        settings.PayrollLiabilityAccountCode = "2100";
        await DictionaryManager.SaveRecordAsync(settings);

        var fiscalYear = DictionaryManager.NewRecord<FiscalYear>();
        fiscalYear.Code = "FY26";
        fiscalYear.StartDate = new DateTime(2026, 1, 1);
        fiscalYear.EndDate = new DateTime(2026, 12, 31);
        fiscalYear.IsClosed = false;
        fiscalYear = await DictionaryManager.SaveRecordAsync(fiscalYear);

        // Два периода, НЕ пересекающихся: попасть можно ровно в один, и какой
        // именно — однозначно определяется датой, попавшей в проводку.
        var past = await PeriodAsync(fiscalYear.MetaId, "P07",
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 31));
        var current = await PeriodAsync(fiscalYear.MetaId, "P09",
            new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));

        return new Setup { Division = division.MetaId, PastPeriod = past, CurrentPeriod = current };
    }

    private Guid Employee;

    private async Task AccountAsync(string code, string name, AccountType type, Guid currency)
    {
        var account = DictionaryManager.NewRecord<ChartOfAccounts>();
        account.Code = code;
        account.Name = name;
        account.AccountType = type;
        account.IsPostable = true;
        account.Currency = currency;
        await DictionaryManager.SaveRecordAsync(account);
    }

    private async Task<Guid> PeriodAsync(Guid year, string code, DateTime from, DateTime to)
    {
        var period = DictionaryManager.NewRecord<FiscalPeriod>();
        period.Code = code;
        period.FiscalYear = year;
        period.FromDate = from;
        period.ToDate = to;
        period.Status = "Open";
        return (await DictionaryManager.SaveRecordAsync(period)).MetaId;
    }

    /// <summary>Проводка, порождённая документом: ищется по графу связей, как это
    /// делают остальные тесты разноски.</summary>
    private static async Task<JournalEntry?> EntryOfAsync(Guid document)
    {
        var family = await DocumentManager.GetDocumentFamilyAsync(document);
        foreach (var childId in family.Edges.Where(e => e.ParentDocId == document)
                                            .Select(e => e.ChildDocId).Distinct())
        {
            var entry = await DocumentManager.GetDocumentAsync<JournalEntry>(childId);
            if (entry != null) return entry;
        }
        return null;
    }

    [IntegrationTest("Проводка задним числом ложится на дату документа, а не на сегодня")]
    public async Task LedgerEntryCarriesDocumentDate()
    {
        var s = await SetupAsync();

        var accrual = await DocumentManager.NewDocumentAsync<PayrollAccrual>();
        accrual.Division = s.Division;
        accrual.DocumentDate = Backdated;
        accrual.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = Employee, Amount = 700m });
        await DocumentManager.SaveDocumentAsync(accrual);
        accrual.Subtype = PayrollAccrual.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(accrual);

        // Сначала убеждаемся, что документ действительно сохранил свою дату:
        // иначе провал следующей проверки означал бы не то, что проверяется.
        var saved = await DocumentManager.GetDocumentAsync<PayrollAccrual>(accrual.MetaId);
        Assert.IsTrue(saved != null && saved.DocumentDate.Date == Backdated,
            "документ обязан сохранить дату {0}, факт {1}",
            Backdated.ToString("yyyy-MM-dd"), saved?.DocumentDate.ToString("yyyy-MM-dd"));

        var entry = await EntryOfAsync(accrual.MetaId);
        Assert.IsTrue(entry != null, "начисление обязано породить проводку в книге");

        Assert.IsTrue(entry!.DocumentDate.Date == Backdated,
            "проводка датируется датой документа {0}, факт {1}",
            Backdated.ToString("yyyy-MM-dd"), entry.DocumentDate.ToString("yyyy-MM-dd"));
    }

    [IntegrationTest("Проводка задним числом попадает в период документа, а не в текущий")]
    public async Task LedgerEntryLandsInDocumentPeriod()
    {
        var s = await SetupAsync();

        var accrual = await DocumentManager.NewDocumentAsync<PayrollAccrual>();
        accrual.Division = s.Division;
        accrual.DocumentDate = Backdated;
        accrual.Lines.Add(new PayrollAccrualLinesTablePartRow { Employee = Employee, Amount = 700m });
        await DocumentManager.SaveDocumentAsync(accrual);
        accrual.Subtype = PayrollAccrual.Subtypes.Posted;
        await DocumentManager.SaveDocumentAsync(accrual);

        var entry = await EntryOfAsync(accrual.MetaId);
        Assert.IsTrue(entry != null, "начисление обязано породить проводку в книге");

        // Период — то, по чему собирается отчётность. Проверяется отдельно от
        // даты: дату можно было бы поправить, забыв, что период выбирается по ней.
        Assert.IsTrue(entry!.FiscalPeriod == s.PastPeriod,
            "проводка обязана попасть в период документа (июль), а не в текущий (сентябрь)");
        Assert.IsTrue(entry.FiscalPeriod != s.CurrentPeriod,
            "проводка не должна попадать в период дня разноски");
    }
}
