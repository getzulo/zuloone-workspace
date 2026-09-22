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
// это TaxReportMapping, датированные ДАННЫЕ: ошибка в номере рядка есть ошибка
// в отчётности, и выдумывать их нельзя. Сервис только агрегирует то, что уже
// проставлено на строках декларации.
public partial class UaTaxFiling
{
    private const string NotMapped = "(не зіставлено)";

    /// <summary>
    /// Тип выгрузки удержаний. Не название бланка ДПС: офіційного 4ДФ тут нет
    /// (немає ознаки доходу 101/102 і нараховано/виплачено). Це робоча таблиця
    /// з UaPayrollLevy за період декларації, щоб бухгалтер переніс цифри руками.
    /// </summary>
    public const string LevyReturnType = "UA-LEVY";

    private readonly IDocumentManager _documents;
    private readonly IDictionaryManager _dictionaries;
    private readonly IDictionaryManager<LegalEntity> _entities;
    private readonly IDictionaryManager<UaTaxFilingExport> _exports;
    private readonly IDictionaryManager<Employee> _employees;
    private readonly IDictionaryManager<TaxCode> _taxCodes;
    private readonly ITotalsManager _totals;

    public UaTaxFiling(
        IDocumentManager documents,
        IDictionaryManager dictionaries,
        IDictionaryManager<LegalEntity> entities,
        IDictionaryManager<UaTaxFilingExport> exports,
        IDictionaryManager<Employee> employees,
        IDictionaryManager<TaxCode> taxCodes,
        ITotalsManager totals)
    {
        _documents = documents;
        _dictionaries = dictionaries;
        _entities = entities;
        _exports = exports;
        _employees = employees;
        _taxCodes = taxCodes;
        _totals = totals;
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

        var payload = BuildPayload(declaration, entity, returnType);

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
