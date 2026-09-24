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
// ДЕНЬ ОПЕРАЦИИ. Пустой ScheduledOn = дата документа: старые заказы без
// кнопки «Расставить сроки» висят на дне заказа, как раньше. Заполненный
// срок кладёт минуты на свой день, и очередь заказа больше не обязана
// целиком сидеть на DocumentDate.
//
// РАССТАНОВКА СРОКОВ. Операции идут по Sequence. Следующий номер может
// начаться в тот же день, если на участке ещё хватает минут; не влезла —
// следующий день. Мощность 0 = потолка нет, сдвига нет. Операция длиннее
// дня остаётся на первом дне и даёт предупреждение: по дням её не режем.
// Параллельные станки и календарь смен сюда не входят: дневная мощность —
// уже одно число, в которое планировщик сам сложил станки и смены.
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
            var full = await _docs.GetDocumentAsync<ProductionOrder>(head.MetaId);
            if (full == null) continue;
            foreach (var op in full.Operations)
            {
                if (op.WorkCenter != workCenter) continue;
                if (op.CompletedOn != null) continue;
                if (ScheduleDay(op, full) != day) continue;
                total += (op.SetupMinutes ?? 0) + (op.RunMinutes ?? 0m);
            }
        }
        return total;
    }

    /// <summary>
    /// Плановые минуты ОДНОГО заказа на участке: неотмеченные операции,
    /// наладка плюс работа. По всем дням срока, не по одному.
    /// </summary>
    public decimal PlannedMinutes(ProductionOrder order, Guid workCenter)
    {
        var total = 0m;
        foreach (var op in order.Operations)
        {
            if (op.WorkCenter != workCenter) continue;
            if (op.CompletedOn != null) continue;
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
    /// Ставит каждой операции день: по Sequence и остатку дневной мощности.
    /// Подтип не меняет. Завершённый заказ и заказ без операций — 0.
    /// Возвращает число операций, которым день записан.
    /// </summary>
    public async Task<int> ScheduleAsync(Guid orderId)
    {
        var order = orderId == Guid.Empty ? null : await _docs.GetDocumentAsync<ProductionOrder>(orderId);
        if (order == null || order.Operations.Count == 0) return 0;
        if (order.Subtype == ProductionOrder.Subtypes.Finished) return 0;

        var start = order.DocumentDate == default ? DateTime.UtcNow.Date : order.DocumentDate.Date;
        var placed = new Dictionary<(Guid Center, DateTime Day), decimal>();
        var capacity = new Dictionary<Guid, decimal>();
        var earliest = start;

        foreach (var group in order.Operations.GroupBy(o => o.Sequence).OrderBy(g => g.Key))
        {
            var groupLast = earliest;
            foreach (var op in group.OrderBy(o => o.Name))
            {
                if (op.CompletedOn != null)
                {
                    var done = op.CompletedOn.Value.Date;
                    if (!(op.ScheduledOn is DateTime already && already.Year >= 1902))
                        op.ScheduledOn = done;
                    if (done > groupLast) groupLast = done;
                    continue;
                }

                var minutes = (op.SetupMinutes ?? 0) + (op.RunMinutes ?? 0m);
                var day = await FirstFitAsync(op.WorkCenter, earliest, minutes, order.MetaId, placed, capacity);
                op.ScheduledOn = day;
                if (op.WorkCenter != Guid.Empty)
                {
                    var key = (op.WorkCenter, day);
                    placed[key] = (placed.TryGetValue(key, out var used) ? used : 0m) + minutes;
                }
                if (day > groupLast) groupLast = day;
            }
            earliest = groupLast;
        }

        await _docs.SaveDocumentAsync(order);
        return order.Operations.Count;
    }

    /// <summary>
    /// Что переполнится, если запустить этот заказ, — текстом, или null.
    /// Это ПРЕДУПРЕЖДЕНИЕ, а не запрет: мощность — план, и запускать сверх неё
    /// законно (сверхурочные, вторая смена). Считается по дню каждой операции.
    /// </summary>
    public async Task<string?> OverloadWarningAsync(Guid orderId)
    {
        var order = orderId == Guid.Empty ? null : await _docs.GetDocumentAsync<ProductionOrder>(orderId);
        if (order == null || order.Operations.Count == 0) return null;

        var mine = new Dictionary<(Guid Center, DateTime Day), decimal>();
        foreach (var op in order.Operations)
        {
            if (op.WorkCenter == Guid.Empty || op.CompletedOn != null) continue;
            var minutes = (op.SetupMinutes ?? 0) + (op.RunMinutes ?? 0m);
            if (minutes <= 0m) continue;
            var key = (op.WorkCenter, ScheduleDay(op, order));
            mine[key] = (mine.TryGetValue(key, out var have) ? have : 0m) + minutes;
        }

        var over = new List<string>();
        foreach (var kv in mine.OrderBy(k => k.Key.Day).ThenBy(k => k.Key.Center))
        {
            var capacity = await CapacityAsync(kv.Key.Center);
            if (capacity <= 0m) continue;
            var queued = await QueuedMinutesAsync(kv.Key.Center, kv.Key.Day, orderId);
            var after = queued + kv.Value;
            if (after <= capacity) continue;

            var center = await _centers.GetRecordAsync(kv.Key.Center);
            over.Add($"{center?.Name ?? "участок"} {kv.Key.Day:yyyy-MM-dd}: {after:0.##} мин при мощности {capacity:0.##}");
        }

        return over.Count == 0 ? null : string.Join("; ", over);
    }

    async Task<DateTime> FirstFitAsync(
        Guid workCenter, DateTime earliest, decimal minutes, Guid orderId,
        Dictionary<(Guid Center, DateTime Day), decimal> placed,
        Dictionary<Guid, decimal> capacityCache)
    {
        if (workCenter == Guid.Empty || minutes <= 0m) return earliest;
        if (!capacityCache.TryGetValue(workCenter, out var capacity))
        {
            capacity = await CapacityAsync(workCenter);
            capacityCache[workCenter] = capacity;
        }
        if (capacity <= 0m || minutes > capacity) return earliest;

        for (var day = earliest; day < earliest.AddDays(366); day = day.AddDays(1))
        {
            var queued = await QueuedMinutesAsync(workCenter, day, orderId);
            var mine = placed.TryGetValue((workCenter, day), out var used) ? used : 0m;
            if (queued + mine + minutes <= capacity) return day;
        }
        return earliest;
    }

    static DateTime ScheduleDay(ProductionOrderOperationsTablePartRow op, ProductionOrder order)
    {
        if (op.ScheduledOn is DateTime scheduled && scheduled.Year >= 1902)
            return scheduled.Date;
        return order.DocumentDate == default ? DateTime.UtcNow.Date : order.DocumentDate.Date;
    }
}
