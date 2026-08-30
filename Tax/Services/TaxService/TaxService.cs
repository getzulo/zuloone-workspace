using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Runtime.Generated;

// Сервис "TaxService": контракт ITaxService. Налоговый расчёт в одном месте —
// разрешение ставки по налоговому коду (TaxCode → TaxRate) и сумма налога
// (база × ставка, округлённая до денежной точности). Ставка хранится долей
// (0.15 = 15%), точность денег — глобальная настройка AmountScale.
//
// CalculateTax — СИНХРОННЫЙ (годится для проводок). ResolveRateAsync — async
// (чтение справочников), для событий/команд/отчётов/API, где нужно подобрать
// действующую ставку по коду.
public partial class TaxService
{
    private readonly IDictionaryManager<TaxCode> _codes;
    private readonly IDictionaryManager<TaxRate> _rates;
    private readonly IDictionaryManager<TaxSettings> _settings;
    private readonly IDictionaryManager<TaxDirection> _directions;
    private readonly IDictionaryManager<LegalEntity> _legalEntities;
    private readonly IDocumentManager _documents;
    private readonly IDocumentPostingService _posting;

    public TaxService(
        IDictionaryManager<TaxCode> codes,
        IDictionaryManager<TaxRate> rates,
        IDictionaryManager<TaxSettings> settings,
        IDictionaryManager<TaxDirection> directions,
        IDictionaryManager<LegalEntity> legalEntities,
        IDocumentManager documents,
        IDocumentPostingService posting)
    {
        _codes = codes;
        _rates = rates;
        _settings = settings;
        _directions = directions;
        _legalEntities = legalEntities;
        _documents = documents;
        _posting = posting;
    }

    /// <summary>Тип документа TaxCalculation — цель перевода в Finalized.</summary>
    private static readonly Guid TaxCalculationType = Guid.Parse("1e07e7a9-d80f-4067-bc65-e40c96d4feee");

    /// <summary>Код налога по умолчанию из настроек модуля; null, если контур не настроен.</summary>
    public async Task<Guid?> ResolveDefaultTaxCodeAsync()
    {
        var settings = (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault();
        if (settings is null || string.IsNullOrWhiteSpace(settings.DefaultTaxCode)) return null;
        return (await _codes.GetRecordsAsync($"Code = '{settings.DefaultTaxCode}'")).FirstOrDefault()?.MetaId;
    }

    /// <summary>
    /// Порождает ПРОВЕДЁННЫЙ расчёт налога на заданную базу и возвращает его id.
    /// Возвращает null, когда налоговый контур не настроен (нет кода по умолчанию,
    /// ставки, направления или юрлица) — это НЕ ошибка: документ-источник обязан
    /// проводиться как раньше на стенде без налогов.
    ///
    /// Здесь, а не в обработчиках счёта и прихода, потому что вход и выход
    /// отличаются ровно кодом направления — всё остальное совпадает, и разъехаться
    /// им нельзя: и то и другое попадает в один леджер и одну декларацию.
    /// </summary>
    public async Task<Guid?> CreateCalculationAsync(
        Guid legalEntity, string directionCode, decimal taxBase, string reason)
    {
        if (taxBase <= 0m || legalEntity == Guid.Empty) return null;

        var taxCode = await ResolveDefaultTaxCodeAsync();
        if (taxCode is null) return null;

        var rate = await ResolveRateAsync(taxCode.Value);
        if (rate is null) return null;

        var direction = (await _directions.GetRecordsAsync($"Code = '{directionCode}'")).FirstOrDefault();
        if (direction is null) return null;

        var le = await _legalEntities.GetRecordAsync(legalEntity);
        if (le is null) return null;

        var calc = await _documents.NewDocumentAsync<TaxCalculation>("Draft", new Dictionary<string, object?>
        {
            ["LegalEntity"] = le.MetaId,
            ["Currency"] = le.Currency,
            ["TaxPointDate"] = DateTime.UtcNow.Date,
            ["DeterminationReason"] = reason,
        });

        calc.Lines.Add(new TaxCalculationLinesTablePartRow
        {
            Direction = direction.MetaId,
            TaxCode = taxCode.Value,
            RateValue = rate.Value,
            TaxBase = taxBase,
            TaxAmount = CalculateTax(taxBase, rate.Value),
        });

        await _documents.SaveDocumentAsync(calc);
        await _posting.SetSubtypeAsync(TaxCalculationType, calc.MetaId, "Finalized");
        return calc.MetaId;
    }

    /// <summary>Сумма налога = база × ставка (доля), округлённая до денежной точности.</summary>
    public decimal CalculateTax(decimal baseAmount, decimal rate)
        => Math.Round(baseAmount * rate, GlobalConstants.Get<int?>("AmountScale") ?? 2, MidpointRounding.AwayFromZero);

    /// <summary>Ставка налога по коду (TaxCode → TaxRate → Rate); null, если код или ставка не найдены.</summary>
    public async Task<decimal?> ResolveRateAsync(Guid taxCodeId)
    {
        var code = await _codes.GetRecordAsync(taxCodeId);
        if (code == null || code.TaxRate == Guid.Empty) return null;
        var rate = await _rates.GetRecordAsync(code.TaxRate);
        return rate?.Rate;
    }

    /// <summary>Сумма налога по коду: подбирает действующую ставку и считает.</summary>
    public async Task<decimal> CalculateByCodeAsync(decimal baseAmount, Guid taxCodeId)
    {
        var rate = await ResolveRateAsync(taxCodeId);
        return rate.HasValue ? CalculateTax(baseAmount, rate.Value) : 0m;
    }
}
