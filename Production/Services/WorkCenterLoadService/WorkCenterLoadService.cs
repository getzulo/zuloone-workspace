using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Сервис «Загрузка рабочего центра»: IWorkCenterLoadService. Сколько минут
// стоит в очереди у участка и влезает ли это в объявленную мощность.
//
// ЧТО СЧИТАЕТСЯ ОЧЕРЕДЬЮ. Только заказы в Released: Draft ещё не запущен, а
// Finished уже отработан. Минуты берутся ПЛАНОВЫЕ (наладка + работа) у
// НЕотмеченных операций — отмеченная операция из очереди уходит, даже если
// заказ целиком ещё не завершён. Поэтому цеховая отметка разгружает участок,
// и это единственная связь между двумя контурами.
//
// ДАТА — ДОКУМЕНТА, и это осознанное ограничение v1. У операции своей даты
// нет, значит вся очередь заказа висит на дате его документа: сервис отвечает
// «сколько стоит на участке в этот день», а не «когда именно оно пойдёт».
// Настоящее расписание (последовательность, параллельные машины, смены) —
// отдельный контур, и врать про него нельзя.
//
// МОЩНОСТЬ НОЛЬ = потолок не объявлен, а НЕ «нулевая мощность»: перегрузки
// тогда не бывает по построению. Умолчание обязано быть рабочим — у всех
// существующих участков поле пустое.
public partial class WorkCenterLoadService
{
    private readonly IDocumentManager _docs;
    private readonly IDictionaryManager<WorkCenter> _centers;

    public WorkCenterLoadService(IDocumentManager docs, IDictionaryManager<WorkCenter> centers)
    {
        _docs = docs;
        _centers = centers;
    }

    /// <summary>
    /// Плановые минуты, стоящие в очереди у участка на дату. Ноль, если
    /// очереди нет. Заказ-исключение (exceptOrder) не учитывается — так
    /// считают «что будет, если этот заказ ещё НЕ запускать».
    /// </summary>
    public async Task<decimal> QueuedMinutesAsync(Guid workCenter, DateTime onDate, Guid exceptOrder)
    {
        if (workCenter == Guid.Empty) return 0m;
        var day = onDate.Date;

        var orders = await _docs.QueryDocumentsAsync<ProductionOrder>(
            $"Subtype = '{ProductionOrder.Subtypes.Released}'");

        var total = 0m;
        foreach (var head in orders)
        {
            if (head.MetaId == exceptOrder) continue;
            var when = head.DocumentDate == default ? DateTime.UtcNow.Date : head.DocumentDate.Date;
            if (when != day) continue;

            var full = await _docs.GetDocumentAsync<ProductionOrder>(head.MetaId);
            if (full == null) continue;
            total += PlannedMinutes(full, workCenter);
        }
        return total;
    }

    /// <summary>
    /// Плановые минуты ОДНОГО заказа на участке: неотмеченные операции,
    /// наладка плюс работа.
    /// </summary>
    public decimal PlannedMinutes(ProductionOrder order, Guid workCenter)
    {
        var total = 0m;
        foreach (var op in order.Operations)
        {
            if (op.WorkCenter != workCenter) continue;
            if (op.CompletedOn != null) continue;      // отмеченное из очереди ушло
            total += (op.SetupMinutes ?? 0) + (op.RunMinutes ?? 0m);
        }
        return total;
    }

    /// <summary>
    /// Мощность участка в минутах в день. Ноль — потолок не объявлен.
    /// </summary>
    public async Task<decimal> CapacityAsync(Guid workCenter)
    {
        if (workCenter == Guid.Empty) return 0m;
        var center = await _centers.GetRecordAsync(workCenter);
        return center?.CapacityMinutesPerDay ?? 0m;
    }

    /// <summary>
    /// Что переполнится, если запустить этот заказ, — текстом, или null.
    /// Это ПРЕДУПРЕЖДЕНИЕ, а не запрет: мощность — план, и запускать сверх неё
    /// законно (сверхурочные, вторая смена). Поэтому вызывающий печатает текст,
    /// а не отказывает.
    /// </summary>
    public async Task<string?> OverloadWarningAsync(Guid orderId)
    {
        var order = orderId == Guid.Empty ? null : await _docs.GetDocumentAsync<ProductionOrder>(orderId);
        if (order == null || order.Operations.Count == 0) return null;

        var day = order.DocumentDate == default ? DateTime.UtcNow.Date : order.DocumentDate.Date;
        var over = new List<string>();

        foreach (var centerId in order.Operations
            .Select(o => o.WorkCenter)
            .Where(id => id != Guid.Empty)
            .Distinct())
        {
            var capacity = await CapacityAsync(centerId);
            if (capacity <= 0m) continue;              // потолок не объявлен

            var mine = PlannedMinutes(order, centerId);
            if (mine <= 0m) continue;
            var queued = await QueuedMinutesAsync(centerId, day, orderId);
            var after = queued + mine;
            if (after <= capacity) continue;

            var center = await _centers.GetRecordAsync(centerId);
            over.Add($"{center?.Name ?? "участок"}: {after:0.##} мин при мощности {capacity:0.##}");
        }

        return over.Count == 0 ? null : string.Join("; ", over);
    }
}
