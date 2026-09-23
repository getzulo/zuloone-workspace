#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Service "UaTaxFiling": декларация → ФАЙЛ для подачи.
//
// ЗАЧЕМ ФАЙЛ, ЕСЛИ ЕСТЬ ПЕЧАТНАЯ ФОРМА. Бухгалтеры клиента подают ДВУМЯ
// способами: ТОВ через M.E.Doc, ФОПов через кабинет ПриватБанка, потому что
// дешевле, — и часть цифр вбивают руками. Печатная форма отвечает на «покажи»,
// файл — на «перенеси, ничего не пересчитывая». Нужны оба, выбрать один нельзя.
//
// ФОРМАТ НЕЙТРАЛЬНЫЙ, И ЭТО ЧЕСТНОЕ ОГРАНИЧЕНИЕ, А НЕ ЛЕНЬ. Схемы ДПС у нас
// нет, а файл, похожий на официальный и им не являющийся, хуже отсутствия
// файла: его отнесут в кабинет и получат отказ. Поэтому выгрузка называется
// выгрузкой, а не «форматом ДПС», и рассчитана на перенос руками.
//
// НОМЕРА ЯЧЕЕК ЗДЕСЬ НЕ ЗАШИТЫ. Какой код в какую строку декларации попадает —
// это TaxReportMapping, датированные ДАННЫЕ, и читаются ПО ReturnType выгрузки.
// Один код (UA-EP5) на форме ЮО — графа 4 рядка 1, на форме ФОП — рядок 06;
// ядро при конфликте оставляет ReturnBox пустым, поэтому штамп со сборки
// декларации для этих форм не годится.
public partial class UaTaxFiling
{
    private const string NotMapped = "(не зіставлено)";

    /// <summary>
    /// Тип выгрузки удержаний. Не название бланка ДПС: офіційного 4ДФ тут нет
    /// (немає ознаки доходу 101/102 і нараховано/виплачено). Це робоча таблиця
    /// з UaPayrollLevy за період декларації, щоб бухгалтер переніс цифри руками.
    /// </summary>
    public const string LevyReturnType = "UA-LEVY";

    /// <summary>
    /// Тип выгрузки единого расчёта. Не название бланка ДПС J050010x:
    /// офіційного розрахунок тут нет (немає додатків, ознак, XML ДПС).
    /// Це робоча таблиця ЄСВ з SocialInsurance + ПДФО/ВЗ з UaPayrollLevy
    /// за період декларації.
    /// </summary>
    public const string QuarterlyReturnType = "UA-QPR";

    /// <summary>
    /// Ідентифікатор форми ДПС для юрособи з 01.08.2026: Податковий розрахунок
    /// (наказ Мінфіну 13.01.2015 № 4 у редакції 07.05.2026 № 243). Не XML-конверт
    /// кабінету: C_REG/C_STI у нас немає. Це рядки розділу I, які є в регістрах.
    /// </summary>
    public const string DpsCalculationType = "J0500111";

    /// <summary>Додаток 4ДФ до J0500111. Графи виплачено/перераховано порожні.</summary>
    public const string Dps4DfType = "J0510411";

    /// <summary>Додаток Д1 (ЄСВ по застрахованих). Категорія ЗО 1, тип нарахувань 1.</summary>
    public const string DpsD1Type = "J0510111";

    /// <summary>
    /// Ідентифікатор форми ДПС для юрособи з 01.11.2024: декларація з ПДВ
    /// (наказ Мінфіну 09.08.2024 № 400). Рядки — з паперової форми, не XML R00xG3.
    /// </summary>
    public const string DpsVatType = "J0200126";

    /// <summary>
    /// Ідентифікатор форми ДПС для юрособи 3 групи: декларація єдиного податку
    /// (наказ Мінфіну 19.06.2015 № 578 у редакції 31.01.2025 № 57). Графа 3 = 3 %,
    /// графа 4 = 5 %. Рядок 2 (подвійна ставка 6/10 %) і додаток МПЗ не заповнюються:
    /// перевищення в леджере йде кодом UA-EP15 (15 %, форма ФОП F0103309 рядок 07).
    /// </summary>
    public const string DpsSingleTaxType = "J0103509";

    /// <summary>
    /// Ідентифікатор форми ДПС для ФОП 3 групи: квартальна декларація єдиного
    /// податку (наказ Мінфіну 19.06.2015 № 578 у редакції 31.01.2025 № 57).
    /// Рядок 05 = 3 %, 06 = 5 %, 07 = 15 % (ПКУ 293.4). Розділ VIII рядок 23 =
    /// 1 % з (05+06+07); окремого коду в леджері немає. Додатки F0133109 / F0133209
    /// порожні.
    /// </summary>
    public const string DpsFopSingleTaxType = "F0103309";

    private static readonly HashSet<string> VatRow9Boxes = new(StringComparer.OrdinalIgnoreCase)
    {
        "1.1", "1.2", "1.3", "2.3.2", "2.3.3",
        "4.1", "4.1.1", "4.2", "4.2.1", "4.3", "4.3.1",
        "6.1", "6.2", "7.1", "7.2.2", "7.2.3", "8",
    };

    private static readonly HashSet<string> VatRow17Boxes = new(StringComparer.OrdinalIgnoreCase)
    {
        "10.1", "10.2", "10.3", "11.1", "11.2", "11.3",
        "12", "13.1", "13.2", "14", "15", "16",
    };

    private readonly IDocumentManager _documents;
    private readonly IDictionaryManager _dictionaries;
    private readonly IDictionaryManager<LegalEntity> _entities;
    private readonly IDictionaryManager<UaTaxFilingExport> _exports;
    private readonly IDictionaryManager<Employee> _employees;
    private readonly IDictionaryManager<Division> _divisions;
    private readonly IDictionaryManager<TaxCode> _taxCodes;
    private readonly IDictionaryManager<LocalizationUkraineSettings> _uaSettings;
    private readonly IDictionaryManager<HRSettings> _hrSettings;
    private readonly ITotalsManager _totals;
    private readonly AnalyticSetService _analytics;
    private readonly IDataService _data;

    public UaTaxFiling(
        IDocumentManager documents,
        IDictionaryManager dictionaries,
        IDictionaryManager<LegalEntity> entities,
        IDictionaryManager<UaTaxFilingExport> exports,
        IDictionaryManager<Employee> employees,
        IDictionaryManager<Division> divisions,
        IDictionaryManager<TaxCode> taxCodes,
        IDictionaryManager<LocalizationUkraineSettings> uaSettings,
        IDictionaryManager<HRSettings> hrSettings,
        ITotalsManager totals,
        AnalyticSetService analytics,
        IDataService data)
    {
        _documents = documents;
        _dictionaries = dictionaries;
        _entities = entities;
        _exports = exports;
        _employees = employees;
        _divisions = divisions;
        _taxCodes = taxCodes;
        _uaSettings = uaSettings;
        _hrSettings = hrSettings;
        _totals = totals;
        _analytics = analytics;
        _data = data;
    }

    /// <summary>
    /// Собрать выгрузку по декларации и сохранить её. Возвращает идентификатор
    /// строки выгрузки или null, если декларации нет.
    ///
    /// ПОВТОРНЫЙ ВЫЗОВ ЗАМЕЩАЕТ, А НЕ ПЛОДИТ. Декларацию пересобирают: уточнили
    /// период, доначислили, поправили сопоставление. Две выгрузки за один период
    /// одного юрлица — это приглашение подать старую.
    /// </summary>
    public async Task<Guid?> ExportAsync(Guid taxReturnId, string returnType)
    {
        var declaration = await _documents.GetDocumentAsync<TaxReturn>(taxReturnId);
        if (declaration is null) return null;

        var entity = declaration.LegalEntity != Guid.Empty
            ? await _entities.GetRecordAsync(declaration.LegalEntity)
            : null;

        var boxes = await BoxesForAsync(returnType, declaration.PeriodTo);
        var payload = string.Equals(returnType, DpsVatType, StringComparison.OrdinalIgnoreCase)
            ? BuildVatDeclarationPayload(declaration, entity, boxes)
            : string.Equals(returnType, DpsSingleTaxType, StringComparison.OrdinalIgnoreCase)
                ? BuildSingleTaxDeclarationPayload(declaration, entity, boxes)
                : string.Equals(returnType, DpsFopSingleTaxType, StringComparison.OrdinalIgnoreCase)
                    ? BuildFopSingleTaxDeclarationPayload(declaration, entity, boxes)
                    : BuildPayload(declaration, entity, returnType);

        var existing = (await _exports.GetRecordsAsync(
                $"LegalEntity = '{declaration.LegalEntity}'"))
            .FirstOrDefault(r => r.PeriodFrom.Date == declaration.PeriodFrom.Date
                              && r.PeriodTo.Date == declaration.PeriodTo.Date
                              && string.Equals(r.ReturnType, returnType, StringComparison.OrdinalIgnoreCase));

        var row = existing ?? _dictionaries.NewRecord<UaTaxFilingExport>();
        row.LegalEntity = declaration.LegalEntity;
        row.ReturnType = returnType;
        row.PeriodFrom = declaration.PeriodFrom;
        row.PeriodTo = declaration.PeriodTo;
        row.Payload = payload;
        row.CreatedOn = DateTime.UtcNow;
        var saved = await _dictionaries.SaveRecordAsync(row);
        return saved.MetaId;
    }

    /// <summary>
    /// Робоча таблиця утримань за період декларації. Повтор за тим самим
    /// юрлицем і періодом заміщує рядок з ReturnType = UA-LEVY, не плодить
    /// другу і не чіпає вивантаження ПДВ.
    /// </summary>
    public async Task<Guid?> ExportLeviesAsync(Guid taxReturnId)
    {
        var declaration = await _documents.GetDocumentAsync<TaxReturn>(taxReturnId);
        if (declaration is null) return null;

        var entity = declaration.LegalEntity != Guid.Empty
            ? await _entities.GetRecordAsync(declaration.LegalEntity)
            : null;

        var rows = await ListLeviesAsync(
            declaration.LegalEntity, declaration.PeriodFrom, declaration.PeriodTo);
        var payload = BuildLevyPayload(declaration, entity, rows);

        var existing = (await _exports.GetRecordsAsync(
                $"LegalEntity = '{declaration.LegalEntity}'"))
            .FirstOrDefault(r => r.PeriodFrom.Date == declaration.PeriodFrom.Date
                              && r.PeriodTo.Date == declaration.PeriodTo.Date
                              && string.Equals(r.ReturnType, LevyReturnType, StringComparison.OrdinalIgnoreCase));

        var row = existing ?? _dictionaries.NewRecord<UaTaxFilingExport>();
        row.LegalEntity = declaration.LegalEntity;
        row.ReturnType = LevyReturnType;
        row.PeriodFrom = declaration.PeriodFrom;
        row.PeriodTo = declaration.PeriodTo;
        row.Payload = payload;
        row.CreatedOn = DateTime.UtcNow;
        var saved = await _dictionaries.SaveRecordAsync(row);
        return saved.MetaId;
    }

    /// <summary>
    /// Робоча таблиця ЄСВ+ПДФО+ВЗ за період декларації. Повтор заміщує
    /// рядок UA-QPR, не чіпає UA-LEVY і вивантаження ПДВ.
    /// </summary>
    public async Task<Guid?> ExportQuarterlyAsync(Guid taxReturnId)
    {
        var declaration = await _documents.GetDocumentAsync<TaxReturn>(taxReturnId);
        if (declaration is null) return null;

        var entity = declaration.LegalEntity != Guid.Empty
            ? await _entities.GetRecordAsync(declaration.LegalEntity)
            : null;

        var esv = await ListEsvAsync(
            declaration.LegalEntity, declaration.PeriodFrom, declaration.PeriodTo);
        var levies = await ListLeviesAsync(
            declaration.LegalEntity, declaration.PeriodFrom, declaration.PeriodTo);
        var payload = BuildQuarterlyPayload(declaration, entity, esv, levies);

        var existing = (await _exports.GetRecordsAsync(
                $"LegalEntity = '{declaration.LegalEntity}'"))
            .FirstOrDefault(r => r.PeriodFrom.Date == declaration.PeriodFrom.Date
                              && r.PeriodTo.Date == declaration.PeriodTo.Date
                              && string.Equals(r.ReturnType, QuarterlyReturnType, StringComparison.OrdinalIgnoreCase));

        var row = existing ?? _dictionaries.NewRecord<UaTaxFilingExport>();
        row.LegalEntity = declaration.LegalEntity;
        row.ReturnType = QuarterlyReturnType;
        row.PeriodFrom = declaration.PeriodFrom;
        row.PeriodTo = declaration.PeriodTo;
        row.Payload = payload;
        row.CreatedOn = DateTime.UtcNow;
        var saved = await _dictionaries.SaveRecordAsync(row);
        return saved.MetaId;
    }

    /// <summary>
    /// Три бланки ДПС за період декларації: J0500111, J0510411, J0510111.
    /// Повертає id рядка J0500111. Повтор заміщує кожен тип окремо.
    /// Виплачено/перераховано не копіюються з нарахованого.
    /// </summary>
    public async Task<Guid?> ExportDpsPayrollAsync(Guid taxReturnId)
    {
        var declaration = await _documents.GetDocumentAsync<TaxReturn>(taxReturnId);
        if (declaration is null) return null;

        var entity = declaration.LegalEntity != Guid.Empty
            ? await _entities.GetRecordAsync(declaration.LegalEntity)
            : null;

        var four = await ListDps4DfAsync(
            declaration.LegalEntity, declaration.PeriodFrom, declaration.PeriodTo);
        var d1 = await ListDpsD1Async(
            declaration.LegalEntity, declaration.PeriodFrom, declaration.PeriodTo);
        var lines = await ListDpsCalculationAsync(
            declaration.LegalEntity, declaration.PeriodFrom, declaration.PeriodTo);

        await SaveExportAsync(declaration, Dps4DfType, BuildDps4DfPayload(declaration, entity, four));
        await SaveExportAsync(declaration, DpsD1Type, BuildDpsD1Payload(declaration, entity, d1));
        return await SaveExportAsync(
            declaration, DpsCalculationType, BuildDpsCalculationPayload(declaration, entity, lines));
    }

    public async Task<List<(string TaxCard, string Name, decimal AccruedIncome, decimal PaidIncome, decimal AccruedPdfo, decimal TransferredPdfo, decimal AccruedVz, decimal TransferredVz, string IncomeSign, string HireDate)>>
        ListDps4DfAsync(Guid legalEntity, DateTime from, DateTime to)
    {
        var result = new List<(string, string, decimal, decimal, decimal, decimal, decimal, decimal, string, string)>();
        var levies = await LevyByEmployeeAsync(legalEntity, from, to);
        if (levies.Count == 0) return result;

        var sign = await IncomeSignAsync();
        var pdfoCode = await SettingsCodeAsync(s => s.IncomeTaxCode);
        var vzCode = await SettingsCodeAsync(s => s.MilitaryLevyCode);

        foreach (var pair in levies.OrderBy(p => p.Key))
        {
            var employee = await _employees.GetRecordAsync(pair.Key);
            decimal accruedIncome = 0m, pdfo = 0m, vz = 0m;
            foreach (var line in pair.Value)
            {
                if (string.Equals(line.Code, pdfoCode, StringComparison.OrdinalIgnoreCase))
                {
                    accruedIncome = line.Base;
                    pdfo = line.Amount;
                }
                else if (string.Equals(line.Code, vzCode, StringComparison.OrdinalIgnoreCase))
                    vz = line.Amount;
            }
            if (accruedIncome == 0m)
                accruedIncome = pair.Value.Select(l => l.Base).DefaultIfEmpty(0m).Max();

            var hire = employee != null && employee.HireDate.Year >= 1902
                && employee.HireDate.Date >= from.Date && employee.HireDate.Date < to.Date.AddDays(1)
                ? employee.HireDate.ToString("dd.MM.yyyy")
                : "";

            result.Add((
                await TaxCardOfAsync(pair.Key),
                employee?.Name ?? "",
                accruedIncome,
                0m,
                pdfo,
                0m,
                vz,
                0m,
                sign,
                hire));
        }

        return result;
    }

    public async Task<List<(string TaxCard, string LastName, string Category, string AccrualType, int Month, int Year, int Days, decimal Gross, decimal Capped, decimal EmployeeEsv, decimal EmployerEsv)>>
        ListDpsD1Async(Guid legalEntity, DateTime from, DateTime to)
    {
        var result = new List<(string, string, string, string, int, int, int, decimal, decimal, decimal, decimal)>();
        var esv = await EsvByEmployeeAsync(legalEntity, from, to);
        var levies = await LevyByEmployeeAsync(legalEntity, from, to);
        var ids = esv.Keys.Union(levies.Keys).OrderBy(id => id);
        var ceiling = await WageCeilingAsync();
        var month = from.Month;
        var year = from.Year;
        var days = DateTime.DaysInMonth(year, month);

        foreach (var id in ids)
        {
            var employee = await _employees.GetRecordAsync(id);
            var pdfoBase = 0m;
            if (levies.TryGetValue(id, out var lines))
                pdfoBase = lines.Select(l => l.Base).DefaultIfEmpty(0m).Max();
            var (empEsv, erEsv) = esv.TryGetValue(id, out var e) ? e : (0m, 0m);
            var capped = ceiling > 0m && pdfoBase > ceiling ? ceiling : pdfoBase;
            result.Add((
                await TaxCardOfAsync(id),
                employee?.Name ?? "",
                "1",
                "1",
                month,
                year,
                days,
                pdfoBase,
                capped,
                empEsv,
                erEsv));
        }

        return result;
    }

    public async Task<List<(string Cell, string Caption, decimal Amount)>>
        ListDpsCalculationAsync(Guid legalEntity, DateTime from, DateTime to)
    {
        var four = await ListDps4DfAsync(legalEntity, from, to);
        var d1 = await ListDpsD1Async(legalEntity, from, to);
        var gross = four.Sum(r => r.AccruedIncome);
        var capped = d1.Sum(r => r.Capped);
        var esv = d1.Sum(r => r.EmployerEsv);
        return new List<(string, string, decimal)>
        {
            ("R092G3", "Працівників, яким нараховано зарплату", four.Count),
            ("R0101G3", "Загальна сума нарахованого доходу", gross),
            ("R01011G3", "Сума нарахованої заробітної плати", gross),
            ("R0102G3", "Дохід у межах максимальної величини ЄСВ", capped),
            ("R01021G3", "Дохід, на який нараховується 22 %", capped),
            ("R0103G3", "Нараховано єдиного внеску", esv),
            ("R01031G3", "Рядок 2.1 × 22 %", esv),
            ("R0107G3", "Єдиний внесок до сплати", esv),
        };
    }

    public string BuildDps4DfPayload(
        TaxReturn declaration,
        LegalEntity? entity,
        List<(string TaxCard, string Name, decimal AccruedIncome, decimal PaidIncome, decimal AccruedPdfo, decimal TransferredPdfo, decimal AccruedVz, decimal TransferredVz, string IncomeSign, string HireDate)> rows)
    {
        var text = new StringBuilder();
        text.AppendLine("# J0510411 Додаток 4ДФ (наказ Мінфіну 07.05.2026 № 243). Не XML кабінету ДПС.");
        text.AppendLine("# Графи виплачено/перераховано порожні: сплати до бюджету в регістрі немає.");
        Header(text, entity, Dps4DfType, declaration);
        text.AppendLine($"R00G01I;{rows.Count(r => r.IncomeSign == "101")}");
        text.AppendLine();
        text.AppendLine("T1RXXXXG02;T1RXXXXG03A;T1RXXXXG03;T1RXXXXG04A;T1RXXXXG04;T1RXXXXG5A;T1RXXXXG5;T1RXXXXG05;T1RXXXXG06D;Name");
        foreach (var row in rows)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0};{1:0.00};{2:0.00};{3:0.00};{4:0.00};{5:0.00};{6:0.00};{7};{8};{9}",
                row.TaxCard, row.AccruedIncome, row.PaidIncome, row.AccruedPdfo, row.TransferredPdfo,
                row.AccruedVz, row.TransferredVz, row.IncomeSign, row.HireDate, row.Name));
        }
        text.AppendLine();
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "R01G03A;{0:0.00}", rows.Sum(r => r.AccruedIncome)));
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "R01G03;{0:0.00}", rows.Sum(r => r.PaidIncome)));
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "R01G04A;{0:0.00}", rows.Sum(r => r.AccruedPdfo)));
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "R01G04;{0:0.00}", rows.Sum(r => r.TransferredPdfo)));
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "R01G5A;{0:0.00}", rows.Sum(r => r.AccruedVz)));
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "R01G5;{0:0.00}", rows.Sum(r => r.TransferredVz)));
        return text.ToString();
    }

    public string BuildDpsD1Payload(
        TaxReturn declaration,
        LegalEntity? entity,
        List<(string TaxCard, string LastName, string Category, string AccrualType, int Month, int Year, int Days, decimal Gross, decimal Capped, decimal EmployeeEsv, decimal EmployerEsv)> rows)
    {
        var text = new StringBuilder();
        text.AppendLine("# J0510111 Додаток Д1 (наказ Мінфіну 07.05.2026 № 243). Не XML кабінету ДПС.");
        text.AppendLine("# T1RXXXXG8=1 (наймані), T1RXXXXG9=1 (нарахування за звітний місяць).");
        Header(text, entity, DpsD1Type, declaration);
        text.AppendLine("T1RXXXXG7S;T1RXXXXG8;T1RXXXXG9;T1RXXXXG101;T1RXXXXG102;T1RXXXXG111S;T1RXXXXG14;T1RXXXXG16;T1RXXXXG17;T1RXXXXG19;T1RXXXXG20;T1RXXXXG21");
        foreach (var row in rows)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0};{1};{2};{3};{4};{5};{6};{7:0.00};{8:0.00};{9:0.00};{10:0.00};1",
                row.TaxCard, row.Category, row.AccrualType, row.Month, row.Year, row.LastName,
                row.Days, row.Gross, row.Capped, row.EmployeeEsv, row.EmployerEsv));
        }
        text.AppendLine();
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "R01G16;{0:0.00}", rows.Sum(r => r.Gross)));
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "R01G17;{0:0.00}", rows.Sum(r => r.Capped)));
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "R01G19;{0:0.00}", rows.Sum(r => r.EmployeeEsv)));
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "R01G20;{0:0.00}", rows.Sum(r => r.EmployerEsv)));
        return text.ToString();
    }

    public string BuildDpsCalculationPayload(
        TaxReturn declaration,
        LegalEntity? entity,
        List<(string Cell, string Caption, decimal Amount)> lines)
    {
        var text = new StringBuilder();
        text.AppendLine("# J0500111 Податковий розрахунок ЮО (наказ Мінфіну 07.05.2026 № 243). Не XML кабінету ДПС.");
        text.AppendLine("# Заповнено розділ I рядками, які є в SocialInsurance і UaPayrollLevy. Розділи II–III порожні.");
        Header(text, entity, DpsCalculationType, declaration);
        text.AppendLine($"HZ;1");
        text.AppendLine($"HZY;{declaration.PeriodFrom:yyyy}");
        text.AppendLine($"HZM;{declaration.PeriodFrom:MM}");
        text.AppendLine($"HNAME;{entity?.Name ?? string.Empty}");
        text.AppendLine($"HTIN;{entity?.TaxRegistrationNumber ?? string.Empty}");
        text.AppendLine("R061G3;1");
        text.AppendLine("R064G3;1");
        text.AppendLine();
        text.AppendLine("Комірка;Назва;Сума");
        foreach (var line in lines)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture, "{0};{1};{2:0.00}", line.Cell, line.Caption, line.Amount));
        }
        return text.ToString();
    }

    /// <summary>
    /// Рухи UaPayrollLevy за період, згорнуті працівник × код. Кортеж
    /// примітивів — збірка контрактів не бачить власних типів моделі.
    /// </summary>
    public async Task<List<(string EmployeeId, string EmployeeName, string TaxCode, decimal Base, decimal Amount)>>
        ListLeviesAsync(Guid legalEntity, DateTime from, DateTime to)
    {
        var result = new List<(string, string, string, decimal, decimal)>();
        if (legalEntity == Guid.Empty) return result;

        var start = from.Date;
        var endExclusive = to.Date.AddDays(1);
        var movements = await _totals.QueryMovementsAsync(
            "UaPayrollLevy",
            $"[LegalEntity] = '{legalEntity}' AND [MovementDate] >= '{start:yyyy-MM-dd HH:mm:ss}' AND [MovementDate] < '{endExclusive:yyyy-MM-dd HH:mm:ss}'");

        var grouped = new Dictionary<(Guid Employee, Guid TaxCode), (decimal Base, decimal Amount)>();
        foreach (var movement in movements)
        {
            var employee = AsGuid(movement, "Employee");
            var taxCode = AsGuid(movement, "TaxCode");
            if (employee == Guid.Empty || taxCode == Guid.Empty) continue;

            var addBase = AsDecimal(movement, "Base");
            var addAmount = AsDecimal(movement, "Amount");
            var key = (employee, taxCode);
            var prev = grouped.TryGetValue(key, out var v) ? v : (0m, 0m);
            grouped[key] = (prev.Item1 + addBase, prev.Item2 + addAmount);
        }

        foreach (var pair in grouped.OrderBy(p => p.Key.Employee).ThenBy(p => p.Key.TaxCode))
        {
            var employee = await _employees.GetRecordAsync(pair.Key.Employee);
            var code = await _taxCodes.GetRecordAsync(pair.Key.TaxCode);
            result.Add((
                employee?.ID ?? pair.Key.Employee.ToString("N")[..8],
                employee?.Name ?? "",
                code?.Code ?? "",
                pair.Value.Item1,
                pair.Value.Item2));
        }

        return result;
    }

    public string BuildLevyPayload(
        TaxReturn declaration,
        LegalEntity? entity,
        List<(string EmployeeId, string EmployeeName, string TaxCode, decimal Base, decimal Amount)> rows)
    {
        var text = new StringBuilder();
        text.AppendLine("# Робоча таблиця утримань ПДФО/ВЗ (не бланк 4ДФ ДПС)");
        text.AppendLine($"Юрособа;{entity?.Name ?? string.Empty}");
        text.AppendLine($"Податковий номер;{entity?.TaxRegistrationNumber ?? string.Empty}");
        text.AppendLine($"Тип;{LevyReturnType}");
        text.AppendLine($"Період;{declaration.PeriodFrom:yyyy-MM-dd};{declaration.PeriodTo:yyyy-MM-dd}");
        text.AppendLine();
        text.AppendLine("Працівник;Код;Код податку;База;Утримано");

        foreach (var row in rows)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0};{1};{2};{3:0.00};{4:0.00}",
                row.EmployeeName, row.EmployeeId, row.TaxCode, row.Base, row.Amount));
        }

        text.AppendLine();
        text.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "Разом утримано;{0:0.00}",
            rows.Sum(r => r.Amount)));
        return text.ToString();
    }

    /// <summary>
    /// Рухи SocialInsurance за період. Юрлицо — не колонка: аналітики
    /// Employee і Division, ExpandAsync, потім Division.LegalEntity.
    /// </summary>
    public async Task<List<(string EmployeeId, string EmployeeName, decimal EmployeeContribution, decimal EmployerContribution)>>
        ListEsvAsync(Guid legalEntity, DateTime from, DateTime to)
    {
        var result = new List<(string, string, decimal, decimal)>();
        if (legalEntity == Guid.Empty) return result;

        var start = from.Date;
        var endExclusive = to.Date.AddDays(1);
        var movements = await _totals.QueryMovementsAsync(
            "SocialInsurance",
            $"[MovementDate] >= '{start:yyyy-MM-dd HH:mm:ss}' AND [MovementDate] < '{endExclusive:yyyy-MM-dd HH:mm:ss}'");
        if (movements.Count == 0) return result;

        var setIds = movements
            .Select(m => AsGuid(m, "AnalyticSetMetaId"))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        var sets = await _analytics.ExpandAsync(setIds);

        var grouped = new Dictionary<Guid, (decimal Employee, decimal Employer)>();
        var divisionEntity = new Dictionary<Guid, Guid>();
        foreach (var movement in movements)
        {
            var setId = AsGuid(movement, "AnalyticSetMetaId");
            if (setId == Guid.Empty || !sets.TryGetValue(setId, out var values)) continue;

            var divisionId = AnalyticGuid(values, "Division");
            if (divisionId == Guid.Empty) continue;
            if (!divisionEntity.TryGetValue(divisionId, out var entityId))
            {
                var division = await _divisions.GetRecordAsync(divisionId);
                entityId = division?.LegalEntity ?? Guid.Empty;
                divisionEntity[divisionId] = entityId;
            }
            if (entityId != legalEntity) continue;

            var employeeId = AnalyticGuid(values, "Employee");
            if (employeeId == Guid.Empty) continue;

            var prev = grouped.TryGetValue(employeeId, out var v) ? v : (0m, 0m);
            grouped[employeeId] = (
                prev.Item1 + AsDecimal(movement, "EmployeeContribution"),
                prev.Item2 + AsDecimal(movement, "EmployerContribution"));
        }

        foreach (var pair in grouped.OrderBy(p => p.Key))
        {
            var employee = await _employees.GetRecordAsync(pair.Key);
            result.Add((
                employee?.ID ?? pair.Key.ToString("N")[..8],
                employee?.Name ?? "",
                pair.Value.Item1,
                pair.Value.Item2));
        }

        return result;
    }

    public string BuildQuarterlyPayload(
        TaxReturn declaration,
        LegalEntity? entity,
        List<(string EmployeeId, string EmployeeName, decimal EmployeeContribution, decimal EmployerContribution)> esv,
        List<(string EmployeeId, string EmployeeName, string TaxCode, decimal Base, decimal Amount)> levies)
    {
        var text = new StringBuilder();
        text.AppendLine("# Робоча таблиця єдиного розрахунку (ЄСВ+ПДФО+ВЗ, не бланк ДПС)");
        text.AppendLine($"Юрособа;{entity?.Name ?? string.Empty}");
        text.AppendLine($"Податковий номер;{entity?.TaxRegistrationNumber ?? string.Empty}");
        text.AppendLine($"Тип;{QuarterlyReturnType}");
        text.AppendLine($"Період;{declaration.PeriodFrom:yyyy-MM-dd};{declaration.PeriodTo:yyyy-MM-dd}");
        text.AppendLine();
        text.AppendLine("## ЄСВ");
        text.AppendLine("Працівник;Код;Частка працівника;Частка роботодавця");
        foreach (var row in esv)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0};{1};{2:0.00};{3:0.00}",
                row.EmployeeName, row.EmployeeId, row.EmployeeContribution, row.EmployerContribution));
        }
        text.AppendLine();
        text.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "Разом ЄСВ працівник;{0:0.00}",
            esv.Sum(r => r.EmployeeContribution)));
        text.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "Разом ЄСВ роботодавець;{0:0.00}",
            esv.Sum(r => r.EmployerContribution)));
        text.AppendLine();
        text.AppendLine("## ПДФО і ВЗ");
        text.AppendLine("Працівник;Код;Код податку;База;Утримано");
        foreach (var row in levies)
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0};{1};{2};{3:0.00};{4:0.00}",
                row.EmployeeName, row.EmployeeId, row.TaxCode, row.Base, row.Amount));
        }
        text.AppendLine();
        text.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "Разом утримано;{0:0.00}",
            levies.Sum(r => r.Amount)));
        text.AppendLine();
        var together = esv.Sum(r => r.EmployeeContribution)
            + esv.Sum(r => r.EmployerContribution)
            + levies.Sum(r => r.Amount);
        text.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "Разом до перенесення;{0:0.00}",
            together));
        return text.ToString();
    }

    private static Guid AnalyticGuid(IReadOnlyDictionary<string, string> values, string analytic)
        => values.TryGetValue(analytic, out var v) && Guid.TryParse(v, out var g) ? g : Guid.Empty;

    private async Task<Guid?> SaveExportAsync(TaxReturn declaration, string returnType, string payload)
    {
        var existing = (await _exports.GetRecordsAsync(
                $"LegalEntity = '{declaration.LegalEntity}'"))
            .FirstOrDefault(r => r.PeriodFrom.Date == declaration.PeriodFrom.Date
                              && r.PeriodTo.Date == declaration.PeriodTo.Date
                              && string.Equals(r.ReturnType, returnType, StringComparison.OrdinalIgnoreCase));

        var row = existing ?? _dictionaries.NewRecord<UaTaxFilingExport>();
        row.LegalEntity = declaration.LegalEntity;
        row.ReturnType = returnType;
        row.PeriodFrom = declaration.PeriodFrom;
        row.PeriodTo = declaration.PeriodTo;
        row.Payload = payload;
        row.CreatedOn = DateTime.UtcNow;
        var saved = await _dictionaries.SaveRecordAsync(row);
        return saved.MetaId;
    }

    private static void Header(StringBuilder text, LegalEntity? entity, string returnType, TaxReturn declaration)
    {
        text.AppendLine($"Юрособа;{entity?.Name ?? string.Empty}");
        text.AppendLine($"Податковий номер;{entity?.TaxRegistrationNumber ?? string.Empty}");
        text.AppendLine($"Тип;{returnType}");
        text.AppendLine($"Період;{declaration.PeriodFrom:yyyy-MM-dd};{declaration.PeriodTo:yyyy-MM-dd}");
        text.AppendLine();
    }

    private async Task<string> TaxCardOfAsync(Guid employeeId)
    {
        try
        {
            var bag = await _data.GetByIdAsync("Employee", employeeId);
            var raw = bag?["TaxCardNumber"];
            return Convert.ToString(raw) ?? "";
        }
        catch
        {
            return "";
        }
    }

    private async Task<string> IncomeSignAsync()
    {
        var rows = await _uaSettings.GetRecordsAsync(take: 1);
        var sign = rows.Count > 0 ? rows[0].PayrollIncomeSign : null;
        return string.IsNullOrWhiteSpace(sign) ? "101" : sign.Trim();
    }

    private async Task<string> SettingsCodeAsync(Func<LocalizationUkraineSettings, string?> pick)
    {
        var rows = await _uaSettings.GetRecordsAsync(take: 1);
        return rows.Count == 0 ? "" : pick(rows[0]) ?? "";
    }

    private async Task<decimal> WageCeilingAsync()
    {
        var rows = await _hrSettings.GetRecordsAsync(take: 1);
        return rows.Count > 0 ? rows[0].SocialInsuranceWageCeiling : 0m;
    }

    private async Task<Dictionary<Guid, List<(string Code, decimal Base, decimal Amount)>>> LevyByEmployeeAsync(
        Guid legalEntity, DateTime from, DateTime to)
    {
        var grouped = new Dictionary<Guid, List<(string, decimal, decimal)>>();
        if (legalEntity == Guid.Empty) return grouped;

        var start = from.Date;
        var endExclusive = to.Date.AddDays(1);
        var movements = await _totals.QueryMovementsAsync(
            "UaPayrollLevy",
            $"[LegalEntity] = '{legalEntity}' AND [MovementDate] >= '{start:yyyy-MM-dd HH:mm:ss}' AND [MovementDate] < '{endExclusive:yyyy-MM-dd HH:mm:ss}'");

        var sums = new Dictionary<(Guid Employee, Guid TaxCode), (decimal Base, decimal Amount)>();
        foreach (var movement in movements)
        {
            var employee = AsGuid(movement, "Employee");
            var taxCode = AsGuid(movement, "TaxCode");
            if (employee == Guid.Empty || taxCode == Guid.Empty) continue;
            var key = (employee, taxCode);
            var prev = sums.TryGetValue(key, out var v) ? v : (0m, 0m);
            sums[key] = (prev.Item1 + AsDecimal(movement, "Base"), prev.Item2 + AsDecimal(movement, "Amount"));
        }

        foreach (var pair in sums)
        {
            var code = await _taxCodes.GetRecordAsync(pair.Key.TaxCode);
            if (!grouped.TryGetValue(pair.Key.Employee, out var list))
                grouped[pair.Key.Employee] = list = new List<(string, decimal, decimal)>();
            list.Add((code?.Code ?? "", pair.Value.Item1, pair.Value.Item2));
        }

        return grouped;
    }

    private async Task<Dictionary<Guid, (decimal Employee, decimal Employer)>> EsvByEmployeeAsync(
        Guid legalEntity, DateTime from, DateTime to)
    {
        var grouped = new Dictionary<Guid, (decimal, decimal)>();
        if (legalEntity == Guid.Empty) return grouped;

        var start = from.Date;
        var endExclusive = to.Date.AddDays(1);
        var movements = await _totals.QueryMovementsAsync(
            "SocialInsurance",
            $"[MovementDate] >= '{start:yyyy-MM-dd HH:mm:ss}' AND [MovementDate] < '{endExclusive:yyyy-MM-dd HH:mm:ss}'");
        if (movements.Count == 0) return grouped;

        var setIds = movements
            .Select(m => AsGuid(m, "AnalyticSetMetaId"))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        var sets = await _analytics.ExpandAsync(setIds);
        var divisionEntity = new Dictionary<Guid, Guid>();
        foreach (var movement in movements)
        {
            var setId = AsGuid(movement, "AnalyticSetMetaId");
            if (setId == Guid.Empty || !sets.TryGetValue(setId, out var values)) continue;
            var divisionId = AnalyticGuid(values, "Division");
            if (divisionId == Guid.Empty) continue;
            if (!divisionEntity.TryGetValue(divisionId, out var entityId))
            {
                var division = await _divisions.GetRecordAsync(divisionId);
                entityId = division?.LegalEntity ?? Guid.Empty;
                divisionEntity[divisionId] = entityId;
            }
            if (entityId != legalEntity) continue;
            var employeeId = AnalyticGuid(values, "Employee");
            if (employeeId == Guid.Empty) continue;
            var prev = grouped.TryGetValue(employeeId, out var v) ? v : (0m, 0m);
            grouped[employeeId] = (
                prev.Item1 + AsDecimal(movement, "EmployeeContribution"),
                prev.Item2 + AsDecimal(movement, "EmployerContribution"));
        }

        return grouped;
    }

    private static Guid AsGuid(Dictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue(field, out var raw) || raw is null) return Guid.Empty;
        return raw is Guid g ? g : Guid.TryParse(Convert.ToString(raw), out var parsed) ? parsed : Guid.Empty;
    }

    private static decimal AsDecimal(Dictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue(field, out var raw) || raw is null) return 0m;
        return raw is decimal d ? d : Convert.ToDecimal(raw);
    }

    /// <summary>
    /// Рядки J0200126 з декларації. 9 і 17 — формули наказу № 21 у редакції № 400,
    /// не сума всього підряд. 18 і 19 — різниця; рядок 20.2 (відшкодування) не
    /// заповнюється: рахунку СЕА ПДВ у нас немає.
    /// </summary>
    public async Task<List<(string Row, string Caption, decimal ColA, decimal ColB)>>
        ListVatDeclarationAsync(TaxReturn declaration)
        => ListVatDeclaration(declaration, await BoxesForAsync(DpsVatType, declaration.PeriodTo));

    public List<(string Row, string Caption, decimal ColA, decimal ColB)>
        ListVatDeclaration(
            TaxReturn declaration,
            IReadOnlyDictionary<(Guid Code, Guid Direction), string>? boxes = null)
    {
        var grouped = GroupByBox(declaration, boxes);

        var rows = new List<(string, string, decimal, decimal)>();
        foreach (var key in grouped.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var pair = grouped[key];
            rows.Add((key, VatRowCaption(key), pair.Base, pair.Tax));
        }

        decimal SumBoxes(HashSet<string> keys)
        {
            decimal sum = 0m;
            foreach (var key in keys)
            {
                if (grouped.TryGetValue(key, out var pair))
                    sum += pair.Tax;
            }
            return sum;
        }

        var row9 = SumBoxes(VatRow9Boxes);
        var row17 = SumBoxes(VatRow17Boxes);
        var payable = row9 > row17 ? row9 - row17 : 0m;
        var negative = row17 > row9 ? row17 - row9 : 0m;
        rows.Add(("9", "Усього податкових зобов'язань (колонка Б)", 0m, row9));
        rows.Add(("17", "Усього податкового кредиту (колонка Б)", 0m, row17));
        rows.Add(("18", "До сплати (рядок 9 − рядок 17)", 0m, payable));
        rows.Add(("19", "Від'ємне значення (рядок 17 − рядок 9). Не рядок 20.2", 0m, negative));
        return rows;
    }

    public string BuildVatDeclarationPayload(
        TaxReturn declaration,
        LegalEntity? entity,
        IReadOnlyDictionary<(Guid Code, Guid Direction), string>? boxes = null)
    {
        var text = new StringBuilder();
        text.AppendLine("# J0200126 Податкова декларація з ПДВ (наказ Мінфіну 09.08.2024 № 400). Не XML кабінету ДПС.");
        text.AppendLine("# Рядки — з форми. Колонка А = база, колонка Б = ПДВ. Рядок 20.2 (відшкодування) порожній.");
        Header(text, entity, DpsVatType, declaration);
        text.AppendLine("Рядок;Назва;Колонка А;Колонка Б");
        foreach (var row in ListVatDeclaration(declaration, boxes))
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0};{1};{2:0.00};{3:0.00}",
                row.Row, row.Caption, row.ColA, row.ColB));
        }
        return text.ToString();
    }

    private static string VatRowCaption(string box) => box switch
    {
        "1.1" => "Операції за основною ставкою 20%",
        "1.2" => "Операції за ставкою 7%",
        "1.3" => "Операції за ставкою 14%",
        "5" => "Звільнені від оподаткування",
        "10.1" => "Придбання зі ставкою 20%",
        "10.2" => "Придбання зі ставкою 7%",
        "10.3" => "Придбання зі ставкою 14%",
        _ => box == NotMapped ? "Не зіставлено" : box,
    };

    /// <summary>
    /// Рядки J0103509. Графа 3 — ставка 3 %, графа 4 — 5 %. Рядок 9 (попередній
    /// період) нуль: декларацію збирають наростаючим підсумком з 1 січня.
    /// Рядок 2 і додаток J0135709 порожні. UA-EP15 на цій формі не зіставлено.
    /// </summary>
    public async Task<List<(string Row, string Caption, decimal Col3, decimal Col4)>>
        ListSingleTaxDeclarationAsync(TaxReturn declaration)
        => ListSingleTaxDeclaration(declaration, await BoxesForAsync(DpsSingleTaxType, declaration.PeriodTo));

    public List<(string Row, string Caption, decimal Col3, decimal Col4)>
        ListSingleTaxDeclaration(
            TaxReturn declaration,
            IReadOnlyDictionary<(Guid Code, Guid Direction), string>? boxes = null)
    {
        var grouped = GroupByBox(declaration, boxes);

        (decimal Base, decimal Tax) Box(string key)
            => grouped.TryGetValue(key, out var pair) ? pair : (0m, 0m);

        var r1c3 = Box("1.3");
        var r1c4 = Box("1.4");
        var r2c3 = Box("2.3");
        var r2c4 = Box("2.4");
        var r3c3 = Box("3.3");
        var r3c4 = Box("3.4");
        var r4c3 = Box("4.3");
        var r4c4 = Box("4.4");
        var row5c3 = r1c3.Base + r2c3.Base + r3c3.Base + r4c3.Base;
        var row5c4 = r1c4.Base + r2c4.Base + r3c4.Base + r4c4.Base;
        var row6c3 = r1c3.Tax;
        var row6c4 = r1c4.Tax;
        var row7c3 = r2c3.Tax + r3c3.Tax + r4c3.Tax;
        var row7c4 = r2c4.Tax + r3c4.Tax + r4c4.Tax;
        var row8c3 = row6c3 + row7c3;
        var row8c4 = row6c4 + row7c4;

        var rows = new List<(string, string, decimal, decimal)>
        {
            ("1", "Обсяг доходу за основною ставкою", r1c3.Base, r1c4.Base),
            ("2", "Дохід понад ліміт (подвійна ставка). Не UA-EP15", r2c3.Base, r2c4.Base),
            ("3", "Негрошові розрахунки", r3c3.Base, r3c4.Base),
            ("4", "Заборонені види діяльності", r4c3.Base, r4c4.Base),
            ("5", "Усього доходу (р.1+р.2+р.3+р.4)", row5c3, row5c4),
            ("6", "Сума єдиного податку (р.1 × ставка)", row6c3, row6c4),
            ("7", "Єдиний податок за подвійною ставкою", row7c3, row7c4),
            ("8", "Усього нараховано (р.6+р.7)", row8c3, row8c4),
            ("9", "Нараховано за попередній період (з 1 січня — нуль)", 0m, 0m),
            ("10", "До сплати за період (рядок 8 − рядок 9)", row8c3, row8c4),
        };

        if (grouped.TryGetValue(NotMapped, out var orphan))
            rows.Add((NotMapped, "Не зіставлено (зокрема UA-EP15 ≠ рядок 2 ЮО)", orphan.Base, orphan.Tax));
        return rows;
    }

    public string BuildSingleTaxDeclarationPayload(
        TaxReturn declaration,
        LegalEntity? entity,
        IReadOnlyDictionary<(Guid Code, Guid Direction), string>? boxes = null)
    {
        var text = new StringBuilder();
        text.AppendLine("# J0103509 Декларація платника єдиного податку 3 групи ЮО (наказ Мінфіну 31.01.2025 № 57). Не XML кабінету ДПС.");
        text.AppendLine("# Графа 3 = 3 %, графа 4 = 5 %. Рядок 2 і додаток МПЗ порожні. Період — наростаючим підсумком з 1 січня.");
        Header(text, entity, DpsSingleTaxType, declaration);
        text.AppendLine("Рядок;Назва;Графа 3 (3%);Графа 4 (5%)");
        foreach (var row in ListSingleTaxDeclaration(declaration, boxes))
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0};{1};{2:0.00};{3:0.00}",
                row.Row, row.Caption, row.Col3, row.Col4));
        }
        return text.ToString();
    }

    /// <summary>
    /// Рядки F0103309. 05/06/07 — дохід за ставками 3/5/15 %. 09/10/11 — податок
    /// з леджера. 23 — 1 % з (05+06+07), не код UA-VZ (той — зарплата). Додатки
    /// ЄСВ за себе і МПЗ порожні.
    /// </summary>
    public async Task<List<(string Row, string Caption, decimal Amount)>>
        ListFopSingleTaxDeclarationAsync(TaxReturn declaration)
        => ListFopSingleTaxDeclaration(declaration, await BoxesForAsync(DpsFopSingleTaxType, declaration.PeriodTo));

    public List<(string Row, string Caption, decimal Amount)>
        ListFopSingleTaxDeclaration(
            TaxReturn declaration,
            IReadOnlyDictionary<(Guid Code, Guid Direction), string>? boxes = null)
    {
        var grouped = GroupByBox(declaration, boxes);
        (decimal Base, decimal Tax) Box(string key)
            => grouped.TryGetValue(key, out var pair) ? pair : (0m, 0m);

        var r05 = Box("05");
        var r06 = Box("06");
        var r07 = Box("07");
        var row08 = r05.Base + r06.Base + r07.Base;
        var row09 = r07.Tax;
        var row10 = r05.Tax;
        var row11 = r06.Tax;
        var row12 = row09 + row10 + row11;
        var row23 = Math.Round(row08 * 0.01m, 2, MidpointRounding.AwayFromZero);

        var rows = new List<(string, string, decimal)>
        {
            ("05", "Обсяг доходу за ставкою 3%", r05.Base),
            ("06", "Обсяг доходу за ставкою 5%", r06.Base),
            ("07", "Обсяг доходу за ставкою 15%", r07.Base),
            ("08", "Усього доходу (р.05+р.06+р.07)", row08),
            ("09", "Сума єдиного податку 15%", row09),
            ("10", "Сума єдиного податку 3%", row10),
            ("11", "Сума єдиного податку 5%", row11),
            ("12", "Усього нараховано (р.09+р.10+р.11)", row12),
            ("13", "Нараховано за попередній період (з 1 січня — нуль)", 0m),
            ("14.1", "До сплати за період (рядок 12 − рядок 13)", row12),
            ("14.2", "Додаток 2 МПЗ. Порожній — землі немає", 0m),
            ("14", "Усього до сплати (р.14.1+р.14.2)", row12),
            ("23", "Військовий збір 1% з (р.05+р.06+р.07)", row23),
            ("24", "ВЗ за попередній період (з 1 січня — нуль)", 0m),
            ("25", "ВЗ до сплати (рядок 23 − рядок 24)", row23),
        };
        return rows;
    }

    public string BuildFopSingleTaxDeclarationPayload(
        TaxReturn declaration,
        LegalEntity? entity,
        IReadOnlyDictionary<(Guid Code, Guid Direction), string>? boxes = null)
    {
        var text = new StringBuilder();
        text.AppendLine("# F0103309 Декларація платника єдиного податку 3 групи ФОП (наказ Мінфіну 31.01.2025 № 57). Не XML кабінету ДПС.");
        text.AppendLine("# Рядок 07 = 15 % (ПКУ 293.4). Рядок 23 = 1 % з доходу. Додатки F0133109 і F0133209 порожні. Період — з 1 січня.");
        Header(text, entity, DpsFopSingleTaxType, declaration);
        text.AppendLine("Рядок;Назва;Сума");
        foreach (var row in ListFopSingleTaxDeclaration(declaration, boxes))
        {
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0};{1};{2:0.00}",
                row.Row, row.Caption, row.Amount));
        }
        return text.ToString();
    }

    private async Task<Dictionary<(Guid Code, Guid Direction), string>> BoxesForAsync(string returnType, DateTime on)
    {
        var result = new Dictionary<(Guid Code, Guid Direction), string>();
        var maps = await _dictionaries.GetRecordsAsync<TaxReportMapping>("1 = 1");
        foreach (var group in maps
            .Where(m => string.Equals(m.ReturnType, returnType, StringComparison.OrdinalIgnoreCase)
                     && IsEffectiveOn(m.EffectiveFrom, m.EffectiveTo, on))
            .GroupBy(m => (m.TaxCode, m.Direction)))
        {
            var letters = group.Select(m => m.ReturnBox)
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .Select(b => b!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (letters.Count == 1)
                result[group.Key] = letters[0];
        }
        return result;
    }

    private static bool IsEffectiveOn(DateTime? from, DateTime? to, DateTime date)
        => (from is null || from.Value.Date <= date.Date)
        && (to is null || date.Date <= to.Value.Date);

    private Dictionary<string, (decimal Base, decimal Tax)> GroupByBox(
        TaxReturn declaration,
        IReadOnlyDictionary<(Guid Code, Guid Direction), string>? boxes)
    {
        string BoxOf(TaxReturnLinesTablePartRow line)
        {
            if (boxes is not null)
            {
                if (boxes.TryGetValue((line.TaxCode, line.Direction), out var mapped)
                    && !string.IsNullOrWhiteSpace(mapped))
                    return mapped.Trim();
                return NotMapped;
            }

            return string.IsNullOrWhiteSpace(line.ReturnBox) ? NotMapped : line.ReturnBox!.Trim();
        }

        return declaration.Lines
            .GroupBy(BoxOf)
            .ToDictionary(
                g => g.Key,
                g => (Base: g.Sum(l => l.TaxBase), Tax: g.Sum(l => l.TaxAmount)),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Сам текст. Шапка — чтобы бухгалтер не искал ІПН и период в другом окне;
    /// дальше строки «ячейка; база; налог», уже сложенные ПО ЯЧЕЙКАМ: в
    /// декларацию переносят итог рядка, а не каждую проводку.
    ///
    /// Разделитель — точка с запятой, числа с точкой и инвариантной культурой:
    /// файл открывают в Excel с украинской локалью, где запятая — десятичный
    /// знак, и запятая-разделитель развалила бы колонки.
    /// </summary>
    public string BuildPayload(TaxReturn declaration, LegalEntity? entity, string returnType)
    {
        var text = new StringBuilder();
        text.AppendLine("# Вивантаження декларації для подання (нейтральний формат, не схема ДПС)");
        text.AppendLine($"Юрособа;{entity?.Name ?? string.Empty}");
        text.AppendLine($"Податковий номер;{entity?.TaxRegistrationNumber ?? string.Empty}");
        text.AppendLine($"Тип декларації;{returnType}");
        text.AppendLine($"Період;{declaration.PeriodFrom:yyyy-MM-dd};{declaration.PeriodTo:yyyy-MM-dd}");
        text.AppendLine();
        text.AppendLine("Рядок;База;Податок");

        // Пустая ячейка НЕ выбрасывается, а показывается отдельной строкой.
        // Молча укороченный файл читается как «в декларации меньше», и ошибку
        // заметят уже после подачи; видимая строка «(не зіставлено)» говорит
        // ровно то, что есть: сопоставление для этого кода не заведено.
        var byBox = declaration.Lines
            .GroupBy(l => string.IsNullOrWhiteSpace(l.ReturnBox) ? NotMapped : l.ReturnBox!.Trim())
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var box in byBox)
        {
            var taxBase = box.Sum(l => l.TaxBase);
            var amount = box.Sum(l => l.TaxAmount);
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture, "{0};{1:0.00};{2:0.00}", box.Key, taxBase, amount));
        }

        text.AppendLine();
        text.AppendLine(string.Format(
            CultureInfo.InvariantCulture, "Разом до сплати;{0:0.00}", declaration.NetPayable));
        return text.ToString();
    }
}
