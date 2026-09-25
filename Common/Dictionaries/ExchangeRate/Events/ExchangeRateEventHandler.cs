#nullable enable
using System;
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Курс валюты датирован, и пересечение окон ОДНОЙ валюты запрещено: иначе
// «курс на дату» перестаёт быть функцией — два ряда отвечают на один вопрос
// по-разному, и какой победит, решает порядок строк в выборке.
//
// Та же дисциплина, что у StandardCost и TaxRate. Разрывы между окнами
// законны: курса на дату может не быть, и это честное «не знаю», а не ноль.
public partial class ExchangeRateEventHandler : TypedDictionaryEventHandler<ExchangeRate>
{
    public override async Task<EventResult> OnBeforeCreateAsync(ExchangeRate record, EventContext context)
    {
        var prior = await next(record, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("ExchangeRate");
        if (record.Currency == Guid.Empty)
        {
            var createId = createDefaults.Pick(createSeed, "Currency");
            if (createId != Guid.Empty) record.Currency = createId;
        }
        if (record.EffectiveFrom.Year < 1902)
        {
            var createDay = createDefaults.PickDay(createSeed, "EffectiveFrom");
            if (createDay.Year >= 1902) record.EffectiveFrom = createDay;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforeSaveAsync(ExchangeRate record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        if (record.Currency == Guid.Empty)
            return EventResult.Cancel("Укажите валюту курса.");

        // Ноль и минус недопустимы: на курс делят. Ноль дал бы деление на ноль
        // при пересчёте ИЗ базовой валюты, минус — отрицательную сумму в книге.
        if (record.RateToBase <= 0m)
            return EventResult.Cancel($"Курс к базовой обязан быть больше нуля, задан {record.RateToBase}.");

        // EffectiveTo у СПРАВОЧНИКА генерится DateTime? (как у StandardCost и
        // TaxCode), хотя тот же необязательный DateTime на ШАПКЕ ДОКУМЕНТА —
        // non-nullable. Поэтому здесь HasValue, а не сравнение с default.
        if (record.EffectiveTo.HasValue && record.EffectiveTo.Value < record.EffectiveFrom)
            return EventResult.Cancel(
                $"Окно курса вывернуто: «действует по» {record.EffectiveTo.Value:yyyy-MM-dd} "
                + $"раньше «действует с» {record.EffectiveFrom:yyyy-MM-dd}.");

        var siblings = await context.GetService<IDictionaryManager<ExchangeRate>>()
            .GetRecordsAsync($"Currency = '{record.Currency}'");

        foreach (var other in siblings)
        {
            if (other.MetaId == record.MetaId) continue;
            if (!Overlaps(record.EffectiveFrom, record.EffectiveTo, other.EffectiveFrom, other.EffectiveTo))
                continue;

            return EventResult.Cancel(
                $"Окна курса одной валюты пересекаются: новое "
                + $"{Window(record.EffectiveFrom, record.EffectiveTo)} и уже заведённое "
                + $"{Window(other.EffectiveFrom, other.EffectiveTo)}. Курс на дату обязан быть один.");
        }

        return EventResult.Ok();
    }

    /// <summary>Пустое «по» — окно открыто вправо, поэтому сравнение идёт с MaxValue.</summary>
    private static bool Overlaps(DateTime aFrom, DateTime? aTo, DateTime bFrom, DateTime? bTo)
    {
        var endA = aTo ?? DateTime.MaxValue;
        var endB = bTo ?? DateTime.MaxValue;
        return aFrom <= endB && bFrom <= endA;
    }

    private static string Window(DateTime from, DateTime? to)
        => to.HasValue
            ? $"{from:yyyy-MM-dd}…{to.Value:yyyy-MM-dd}"
            : $"с {from:yyyy-MM-dd} (открыто)";
}
