using System;
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

    public TaxService(IDictionaryManager<TaxCode> codes, IDictionaryManager<TaxRate> rates)
    {
        _codes = codes;
        _rates = rates;
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
