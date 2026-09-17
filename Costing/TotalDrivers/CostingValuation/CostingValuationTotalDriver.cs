#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
using ZuloOne.Totals;
using ZuloOne.Totals.Calculation;

// ═══ ДРАЙВЕР ОЦЕНКИ ВЫБЫТИЯ регистра ItemCostFifo ════════════════════════════
//
// Регистр хранит слои (лоты) прихода; движок зовёт CalculateOutcomes, чтобы
// РАСПЛАНИРОВАТЬ, из каких слоёв и по какой цене снять расход. Метод берётся из
// singleton-справочника CostingSettings и НЕ зашит в код:
//
//   CostingMethod = FIFO (умолчание, в том числе когда записи настроек нет) —
//     база FifoTotalDriver: расход гасит старейшие лоты, себестоимость лота
//     переходит в выбытие как есть.
//
//   CostingMethod = AVG — средневзвешенная: единица оценивается как
//     Σ сумм открытых лотов / Σ их количеств, а количество снимается с лотов
//     ПРОПОРЦИОНАЛЬНО их остаткам. Пропорция здесь не украшение, а условие
//     сходимости: физический слой хранит ИСХОДНУЮ цену партии, и его
//     непогашенная стоимость всегда считается как Amount/OriginalQty×RemainingQty
//     (платформа гасит только RemainingQty). Гаси лоты по старшинству, а оценивай
//     по средней — сумма непогашенных стоимостей слоёв разойдётся с остатком
//     регистра ровно на разницу методов. При пропорциональном гашении доли всех
//     лотов уменьшаются одинаково, и обе величины остаются равными.
//
//   RoundCosts — округлять себестоимость выбытия до 2 знаков (тот же хук
//     CalculatePartialAmount, что и у стендового TBRounding).
//
// ГДЕ читаются настройки. В ИНИЦИАЛИЗАТОРЕ ПОЛЯ, то есть в конструкторе, то есть
// в момент ITotalDriverProvider.ResolveAsync — единственной точке жизни драйвера,
// когда соединение регистра ЕЩЁ не открыто (RegisterMovementService резолвит
// драйвер строкой раньше connection.OpenAsync). Все хуки расчёта —
// LoadTotalState, CalculateOutcomes — платформа зовёт уже с открытым
// соединением: второе соединение внутри той же окружающей транзакции повысило бы
// её до распределённой, чего сервер стенда не поддерживает. Экземпляр драйвера
// живёт одно движение, поэтому «один раз в конструкторе» — это и есть «свежие
// настройки на каждое движение».
public partial class CostingValuationTotalDriver
{
    private readonly (bool Average, bool Round) _settings = ReadSettings();

    /// <summary>Метод и округление из CostingSettings; нет записи — FIFO без округления.</summary>
    private static (bool Average, bool Round) ReadSettings()
    {
        var rows = GetService<IDictionaryManager>()
            .GetRecordsAsync<CostingSettings>(null, 1).GetAwaiter().GetResult();
        if (rows.Count == 0) return (false, false);
        return (string.Equals(rows[0].CostingMethod, "AVG", StringComparison.OrdinalIgnoreCase), rows[0].RoundCosts);
    }

    private decimal Round(decimal value)
        => _settings.Round ? Math.Round(value, 2, MidpointRounding.AwayFromZero) : value;

    /// <summary>FIFO-ветка: частичная себестоимость лота с округлением по настройке.</summary>
    protected override decimal CalculatePartialAmount(decimal lotQuantity, decimal lotAmount, decimal transQuantity)
        => Round(base.CalculatePartialAmount(lotQuantity, lotAmount, transQuantity));

    public override ICollection<DetailedTransactionValue> CalculateOutcomes(
        TransactionValue tv, IEnumerable<TransactionValue> incomes)
    {
        if (!_settings.Average) return base.CalculateOutcomes(tv, incomes);

        var need = -tv.GetValue(QuantityVariableName);
        if (need <= 0m) return base.CalculateOutcomes(tv, incomes);

        var key = GetKeyValue(tv);
        var queue = FindQueue(key);
        var lots = queue == null ? new List<DetailedTransactionValue>() : queue.ToList();
        var haveQty = lots.Sum(l => l.GetValue(QuantityVariableName));

        // Нехватка слоёв — не дело метода оценки. Отдаём базе: она сформирует
        // лот «списание ниже нуля», и движок отклонит перерасход, как и в FIFO.
        if (haveQty < need) return base.CalculateOutcomes(tv, incomes);

        var haveAmount = lots.Sum(l => l.GetValue(AmountVariableName));
        var unit = haveAmount / haveQty;
        var totalCost = Round(unit * need);

        // Доли: остаток лота × потребность / всего. Последнему лоту достаётся
        // ОСТАТОК потребности — так сумма долей сходится с целым и на «хвостах»
        // деления.
        var takes = new decimal[lots.Count];
        var left = need;
        for (var i = 0; i < lots.Count && left > 0m; i++)
        {
            var lotQty = lots[i].GetValue(QuantityVariableName);
            var share = i == lots.Count - 1 ? left : lotQty * need / haveQty;
            if (share > lotQty) share = lotQty;
            if (share > left) share = left;
            takes[i] = share;
            left -= share;
        }
        // Хвост после клампов раскидываем по лотам с оставшейся ёмкостью.
        for (var i = 0; i < lots.Count && left > 0m; i++)
        {
            var room = lots[i].GetValue(QuantityVariableName) - takes[i];
            if (room <= 0m) continue;
            var add = room < left ? room : left;
            takes[i] += add;
            left -= add;
        }

        var outcomes = new List<DetailedTransactionValue>();
        var costLeft = totalCost;
        var lastIndex = Array.FindLastIndex(takes, t => t > 0m);
        var survivors = new Queue<DetailedTransactionValue>();
        for (var i = 0; i < lots.Count; i++)
        {
            var lot = lots[i];
            var lotQty = lot.GetValue(QuantityVariableName);
            var lotAmount = lot.GetValue(AmountVariableName);
            var take = takes[i];
            if (take > 0m)
            {
                // Последней доле достаётся остаток стоимости: иначе округление
                // каждой доли отдельно увело бы сумму от totalCost.
                var cost = i == lastIndex ? costLeft : Round(unit * take);
                costLeft -= cost;

                var detail = tv.CreateDetailedTransactionValue();
                detail.SetValue(QuantityVariableName, -take);
                detail.SetValue(AmountVariableName, -cost);
                detail.LotNo = lot.LotNo;
                detail.DeltaSubNo = GetNextDeltaSubNo(detail.DeltaNo);
                // Неоперационные измерения лот несёт сам (как в базовом FIFO).
                foreach (var name in detail.TotalDescriptor.Dimensions.Where(d => !d.IsOperational).Select(d => d.Name))
                    detail.SetCoordinate(name, lot.GetCoordinate(name));
                outcomes.Add(detail);

                lot.SetValue(QuantityVariableName, lotQty - take);
                lot.SetValue(AmountVariableName, lotAmount - cost);
            }
            if (lot.GetValue(QuantityVariableName) > 0m) survivors.Enqueue(lot);
        }

        // Очередь лотов пересобирается: гашение шло по всем лотам сразу, а
        // RemoveFifoLot умеет снимать только с головы.
        if (survivors.Count > 0) QueueIndex[key] = survivors;
        else QueueIndex.Remove(key);

        return outcomes;
    }
}
