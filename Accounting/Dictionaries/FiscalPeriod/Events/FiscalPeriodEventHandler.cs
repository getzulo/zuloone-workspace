#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;

namespace ZuloOne.Runtime.Generated;

// ЦЕЛОСТНОСТЬ КАЛЕНДАРЯ УЧЁТНЫХ ПЕРИОДОВ.
//
// Период — окно дат, по которому собирается отчётность, и проводка выбирает его
// ПО ДАТЕ. Отсюда два требования, которых метаданными не выразить:
//
//  1. На любую дату приходится РОВНО ОДИН период. Пересекутся два — подбор
//     вернёт случайный: часть проводок уйдёт в один месяц, часть в другой, и
//     заметно это станет только при сверке. Отказ ставится на ВВОДЕ, где человек
//     видит соседние строки. GeneralLedgerService при двух подходящих периодах
//     тоже отказывает — но узнаёт об этом уже при разноске, то есть ошибку
//     мастер-данных ловила бы операция.
//
//  2. Периоды закрываются ПО ПОРЯДКУ. «Февраль закрыт, январь открыт» — не
//     календарь, а дырка: платформенный запрет проведения выражается ОДНОЙ
//     датой-границей, и такое состояние в неё не отображается. Разрешить его
//     значит гарантированно разъехаться с платформой.
public partial class FiscalPeriodEventHandler : TypedDictionaryEventHandler<FiscalPeriod>
{
    /// <summary>Единственный статус, означающий «период принимает проводки».
    /// Набор закрытый, и его место в метаданных перечислением; пока это строка —
    /// сравнение регистронезависимое, а всё незнакомое считается ЗАКРЫТЫМ:
    /// опечатка в статусе обязана запрещать проводку, а не разрешать её.</summary>
    private const string OpenStatus = "Open";

    private static bool IsOpen(FiscalPeriod period)
        => string.Equals(period.Status, OpenStatus, StringComparison.OrdinalIgnoreCase);

    public override async Task<EventResult> OnBeforeSaveAsync(FiscalPeriod record, bool isNew, EventContext context)
    {
        if (record.FromDate > record.ToDate)
            return EventResult.Cancel("Начало периода должно быть не позже конца");

        var periods = context.GetService<IDictionaryManager<FiscalPeriod>>();
        var siblings = (await periods.GetRecordsAsync())
            .Where(p => p.MetaId != record.MetaId)
            .ToList();

        var clash = siblings.FirstOrDefault(p =>
            record.FromDate.Date <= p.ToDate.Date && p.FromDate.Date <= record.ToDate.Date);
        if (clash != null)
            return EventResult.Cancel(
                $"Окно периода пересекается с периодом «{clash.Code}» "
                + $"({clash.FromDate:yyyy-MM-dd} — {clash.ToDate:yyyy-MM-dd}). "
                + "На каждую дату должен приходиться ровно один период.");

        // Порядок проверяется только при ЗАКРЫТИИ: уже закрытый период можно
        // пересохранить (правка кода, подписи), не упираясь в календарь.
        if (!IsOpen(record))
        {
            var earlierOpen = siblings
                .Where(p => p.ToDate.Date < record.FromDate.Date && IsOpen(p))
                .OrderByDescending(p => p.ToDate)
                .FirstOrDefault();

            if (earlierOpen != null)
                return EventResult.Cancel(
                    $"Нельзя закрыть период: более ранний период «{earlierOpen.Code}» "
                    + $"({earlierOpen.FromDate:yyyy-MM-dd} — {earlierOpen.ToDate:yyyy-MM-dd}) "
                    + "ещё открыт. Периоды закрываются по порядку.");
        }

        return EventResult.Ok();
    }
}
