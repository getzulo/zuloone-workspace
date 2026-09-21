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

    private readonly IDocumentManager _documents;
    private readonly IDictionaryManager _dictionaries;
    private readonly IDictionaryManager<LegalEntity> _entities;
    private readonly IDictionaryManager<UaTaxFilingExport> _exports;

    public UaTaxFiling(
        IDocumentManager documents,
        IDictionaryManager dictionaries,
        IDictionaryManager<LegalEntity> entities,
        IDictionaryManager<UaTaxFilingExport> exports)
    {
        _documents = documents;
        _dictionaries = dictionaries;
        _entities = entities;
        _exports = exports;
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
