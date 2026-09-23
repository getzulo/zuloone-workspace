using System;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Курс валюты на дату и перевод сумм между валютами.
//
// МОДЕЛЬ — ТА ЖЕ, ЧТО У ЕДИНИЦ ИЗМЕРЕНИЯ, и это не совпадение. У каждой валюты
// есть датированный коэффициент к базовой (RateToBase), перевод —
// amount × from / to. Попарные курсы («доллар→гривна» отдельной строкой) здесь
// отвергнуты по тем же трём причинам, по которым 2026-08-31 отсюда убрали
// попарные UnitConversion:
//   • ТРАНЗИТИВНОСТЬ БЕСПЛАТНА: евро → злотый считается без правила
//     «евро-злотый», потому что обе валюты выражены через базовую;
//   • N ЧИСЕЛ ВМЕСТО N², и противоречивую тройку (EUR→UAH ≠ EUR→USD × USD→UAH)
//     стало нечем выразить — а на курсах она возникает при первой же правке;
//   • базовую валюту не надо НИГДЕ настраивать: базовая — та, у которой курс 1,
//     ровно как у UnitOfMeasure нет поля «я базовая».
//
// ДАТА ОБЯЗАТЕЛЬНА И НЕ ИМЕЕТ УМОЛЧАНИЯ. Курс «сейчас» для документа прошлого
// месяца — это тихо неверная сумма в книге, поэтому параметр не необязательный:
// вызывающий обязан сказать, на какой день он спрашивает. Тот же урок, что с
// датой проведения в GL.
//
// НЕТ КУРСА — null, А НЕ ЕДИНИЦА. Единица означала бы «валюты равны», то есть
// молча положила бы в книгу сумму в чужой валюте. Отказ ловит вызывающий.
public partial class CurrencyRateService
{
    private readonly IDictionaryManager<ExchangeRate> _rates;

    public CurrencyRateService(IDictionaryManager<ExchangeRate> rates) => _rates = rates;

    /// <summary>
    /// Курс валюты к базовой на дату, или null, если окна на эту дату нет.
    /// Разрывы между окнами законны — это честное «не знаю», а не ноль.
    /// </summary>
    public async Task<decimal?> RateToBaseAsync(Guid currency, DateTime onDate)
    {
        if (currency == Guid.Empty) return null;
        var day = onDate.Date;

        var row = (await _rates.GetRecordsAsync($"Currency = '{currency}'"))
            .FirstOrDefault(r => r.EffectiveFrom.Date <= day
                              && (!r.EffectiveTo.HasValue || r.EffectiveTo.Value.Date >= day));

        return row is null || row.RateToBase <= 0m ? null : row.RateToBase;
    }

    /// <summary>
    /// Перевод суммы между валютами на дату. Null — перевести нечем: у одной из
    /// валют нет окна на эту дату.
    ///
    /// Тождество возвращает СУММУ, а не null и не поход в справочник: валюта
    /// сама в себя — корректный перевод, и требовать для него строку курса
    /// значило бы, что одновалютный тенант обязан завести курс самому себе.
    /// </summary>
    public async Task<decimal?> ConvertAsync(decimal amount, Guid fromCurrency, Guid toCurrency, DateTime onDate)
    {
        if (fromCurrency == toCurrency) return amount;
        if (fromCurrency == Guid.Empty || toCurrency == Guid.Empty) return null;

        var from = await RateToBaseAsync(fromCurrency, onDate);
        var to = await RateToBaseAsync(toCurrency, onDate);
        if (from is null || to is null) return null;

        return amount * from.Value / to.Value;
    }

    /// <summary>
    /// Множитель, на который надо умножить сумму в исходной валюте, чтобы
    /// получить целевую. Нужен там, где пересчёт делает не сервис, а код без
    /// асинхронных чтений: транзакционный скрипт умножить умеет, сходить за
    /// курсом — нет. Тождество даёт 1.
    /// </summary>
    public async Task<decimal?> FactorAsync(Guid fromCurrency, Guid toCurrency, DateTime onDate)
        => await ConvertAsync(1m, fromCurrency, toCurrency, onDate);
}
