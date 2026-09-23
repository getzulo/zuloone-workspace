using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Сервис «Труд заказа»: ILaborCostService. Сколько стоит цеховая работа,
// отмеченная на операциях маршрута, и годится ли отметка к проведению.
//
// ДВЕ ДВЕРИ — ОДИН ПРЕДИКАТ. Отрицательный факт минут удешевляет выпуск, и
// проверять его надо и на проведении заказа, и перед оценкой. Обе двери зовут
// ProblemAsync: правило живёт здесь, а не копией в обработчике.
//
// СТАВКА. Named operator wins: минуты считаются по часовой ставке ДОЛЖНОСТИ
// исполнителя (HR.Position.HourlyRate) — по той же цифре, по которой ему
// начисляют ФОТ, поэтому отнесённый на изделие труд и расход на оплату труда
// говорят об одном человеке. Исполнитель не указан — ставка рабочего центра
// (нормативное поглощение). Нет ни того, ни другого — ноль: труд не
// поглощается, а не падает проведение. Это важно для обратной совместимости —
// у существующих заказов операции не отмечены вовсе.
//
// ПРИЗНАК «ОТМЕЧЕНО» — CompletedOn, не ActualMinutes: мгновенная операция с
// нулём минут законна, и по нулю её не отличить от неотмеченной.
//
// НЕОБЯЗАТЕЛЬНЫЕ ПОЛЯ ТАБЛИЧНОЙ ЧАСТИ NULLABLE, в отличие от полей справочника:
// платформа генерит `decimal?` / `int?`. Поэтому «не отмечено» — это null, а не
// default, и арифметика идёт через `?? 0`.
public partial class LaborCostService
{
    private readonly IDocumentManager _docs;
    private readonly IDictionaryManager<WorkCenter> _centers;
    private readonly IDictionaryManager<Employee> _employees;
    private readonly IDictionaryManager<Position> _positions;

    public LaborCostService(
        IDocumentManager docs,
        IDictionaryManager<WorkCenter> centers,
        IDictionaryManager<Employee> employees,
        IDictionaryManager<Position> positions)
    {
        _docs = docs;
        _centers = centers;
        _employees = employees;
        _positions = positions;
    }

    /// <summary>
    /// Стоимость отмеченных операций заказа. Ноль, если не отмечено ничего —
    /// заказ без цеховой отметки стоит ровно столько же, сколько и раньше.
    /// </summary>
    public async Task<decimal> OrderLaborCostAsync(Guid orderId)
    {
        var order = orderId == Guid.Empty ? null : await _docs.GetDocumentAsync<ProductionOrder>(orderId);
        if (order == null) return 0m;

        var total = 0m;
        foreach (var op in order.Operations)
        {
            if (op.CompletedOn == null) continue;
            var minutes = op.ActualMinutes ?? 0m;
            if (minutes <= 0m) continue;
            var rate = await RateAsync(op.Employee, op.WorkCenter);
            if (rate <= 0m) continue;
            total += Math.Round(minutes / 60m * rate, 2, MidpointRounding.AwayFromZero);
        }
        return total;
    }

    /// <summary>
    /// Что не так с цеховой отметкой заказа, или null. Возвращает имя
    /// конкретной операции: «одна из строк неверна» не даёт её найти.
    /// </summary>
    public async Task<string?> ProblemAsync(Guid orderId)
    {
        var order = orderId == Guid.Empty ? null : await _docs.GetDocumentAsync<ProductionOrder>(orderId);
        if (order == null) return null;

        foreach (var op in order.Operations)
        {
            var minutes = op.ActualMinutes ?? 0m;
            if (minutes < 0m)
                return $"Операция «{op.Sequence} {op.Name}»: факт минут отрицательный ({minutes}).";
            if (op.CompletedOn == null && minutes > 0m)
                return $"Операция «{op.Sequence} {op.Name}»: указан факт минут, но не проставлена дата отметки.";
        }
        return null;
    }

    /// <summary>
    /// Часовая ставка операции. Должность исполнителя перебивает рабочий центр.
    /// </summary>
    public async Task<decimal> RateAsync(Guid employee, Guid workCenter)
    {
        if (employee != Guid.Empty)
        {
            var person = await _employees.GetRecordAsync(employee);
            if (person != null && person.Position != Guid.Empty)
            {
                var position = await _positions.GetRecordAsync(person.Position);
                if (position != null && position.HourlyRate > 0m) return position.HourlyRate;
            }
        }

        if (workCenter != Guid.Empty)
        {
            var center = await _centers.GetRecordAsync(workCenter);
            if (center != null && center.HourlyRate > 0m) return center.HourlyRate;
        }

        return 0m;
    }
}
