#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Service "UaPayrollLevies": что удерживается ИЗ зарплаты в Украине.
//
// ТРИ ПЛАТЕЖА, А НЕ ОДИН, И ОНИ РАЗНОЙ ПРИРОДЫ:
//   ЄСВ  22%  — платит РАБОТОДАТЕЛЬ сверх зарплаты, из кармана работника не
//               берётся ничего. Это в точности то, что моделирует контур
//               SocialInsurance в HR, поэтому здесь его НЕТ: ЄСВ настраивается
//               ставками в HRSettings и никакого кода не требует.
//   ПДФО 18%  — УДЕРЖИВАЕТСЯ у работника.
//   ВЗ    5%  — УДЕРЖИВАЕТСЯ у работника (до 01.12.2024 было 1,5%).
//
// ПОЧЕМУ ПДФО И ВЗ НЕ СВЁРНУТЫ В ОДНУ СУММУ 23%. Свернуть арифметически можно,
// подать — нельзя: сборы уходят в разные бюджеты и в разные строки расчёта, а
// ставка ВЗ менялась отдельно от ПДФО и ещё поменяется. Одна колонка «удержано»
// означала бы, что при следующей смене ставки цифры прошлых периодов
// пересчитать не из чего.
//
// БАЗА У НИХ НЕ ТА, ЧТО У ЄСВ. У ЄСВ есть максимальная база (15 минимальных
// зарплат) — и она уже применена в HR, в поле ContributoryBase строки
// соцвзноса. У ПДФО и ВЗ потолка НЕТ ВОВСЕ. Поэтому считаем от ПОЛНОГО
// начисления, а не от базы соцвзноса: взять готовую базу было бы соблазнительно
// и неверно ровно для тех, у кого зарплата выше потолка.
//
// ВЫКЛЮЧАТЕЛЬ — КОД НАЛОГА В НАСТРОЙКАХ, А НЕ СТРАНА ЮРЛИЦА. Модель не гадает,
// «украинское ли» юрлицо: не заполнен IncomeTaxCode — удержаний нет. Так же
// включены и остальные контуры этой модели, и так же он молчит на стенде, где
// рядом живут чужие локализации.
public partial class UaPayrollLevies
{
    private readonly IDictionaryManager<LocalizationUkraineSettings> _settings;
    private readonly IDictionaryManager<TaxCode> _codes;
    private readonly IDictionaryManager<Division> _divisions;

    public UaPayrollLevies(
        IDictionaryManager<LocalizationUkraineSettings> settings,
        IDictionaryManager<TaxCode> codes,
        IDictionaryManager<Division> divisions)
    {
        _settings = settings;
        _codes = codes;
        _divisions = divisions;
    }

    /// <summary>
    /// Одно удержание описывается КОРТЕЖОМ примитивов, а не своим классом, и
    /// это не вопрос стиля: сборка контрактов собирается раньше моделей и видит
    /// только примитивы. Собственный тип в публичной сигнатуре роняет генерацию
    /// контрактов ВСЕХ сервисов стенда, а не только этого.
    /// </summary>

    /// <summary>
    /// Юрлицо начисления. В документах ФОТ его нет — есть подразделение, и
    /// юрлицо достаётся через него. Так же его достаёт и проводка в GL, и это
    /// не совпадение: другого пути из документа ФОТ к юрлицу не существует.
    /// </summary>
    public async Task<Guid> LegalEntityOfDivisionAsync(Guid divisionId)
    {
        if (divisionId == Guid.Empty) return Guid.Empty;
        var division = await _divisions.GetRecordAsync(divisionId);
        return division?.LegalEntity ?? Guid.Empty;
    }

    /// <summary>
    /// Удержания с одного начисления на дату. Пустой список означает «контур не
    /// настроен» — это нормальное состояние, а не ошибка: тенант без украинской
    /// зарплаты не обязан заводить коды.
    ///
    /// Ставка берётся НА ДАТУ НАЧИСЛЕНИЯ, а не на сегодня. Зарплату за ноябрь
    /// 2024-го, проведённую в декабре, военный сбор обязан взять по 1,5%:
    /// ставка 5% действует с 1 декабря и к ноябрьскому доходу отношения не
    /// имеет. Дата документа здесь — не украшение, а единственный способ
    /// провести перерасчёт за прошлый период и не соврать.
    /// </summary>
    public async Task<List<(Guid TaxCodeId, string Code, decimal Rate, decimal Base, decimal Amount)>>
        WithholdingsAsync(decimal grossAmount, DateTime onDate)
    {
        var result = new List<(Guid, string, decimal, decimal, decimal)>();
        if (grossAmount <= 0m) return result;

        var settings = (await _settings.GetRecordsAsync("1 = 1")).FirstOrDefault();
        if (settings is null) return result;

        foreach (var code in new[] { settings.IncomeTaxCode, settings.MilitaryLevyCode })
        {
            var levy = await LevyAsync(code, grossAmount, onDate);
            if (levy != null) result.Add(levy.Value);
        }

        return result;
    }

    /// <summary>
    /// Нулевая ставка — это НЕ «ставка не найдена»: освобождение существует и
    /// значит «удержать ноль». Но движение на ноль писать незачем, поэтому
    /// наружу отдаём null и в обоих случаях ничего не удерживаем. Разница
    /// осталась бы важной, если бы мы отчитывались о необлагаемом доходе, — на
    /// это в 4ДФ отдельные признаки, и они сюда не входят.
    /// </summary>
    private async Task<(Guid TaxCodeId, string Code, decimal Rate, decimal Base, decimal Amount)?>
        LevyAsync(string? code, decimal grossAmount, DateTime onDate)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var taxCode = (await _codes.GetRecordsAsync($"Code = '{code}'")).FirstOrDefault();
        if (taxCode is null) return null;

        var tax = ScriptServices.Get<ITaxService>();
        var rate = await tax.ResolveRateAsync(taxCode.MetaId, onDate) ?? 0m;
        if (rate <= 0m) return null;

        var amount = tax.CalculateTax(grossAmount, rate);
        if (amount == 0m) return null;

        return (taxCode.MetaId, taxCode.Code, rate, grossAmount, amount);
    }
}
