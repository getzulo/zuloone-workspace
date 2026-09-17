#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
// ZuloOne.Managers и ZuloOne.Totals целиком не открываются: имена
// TransactionCollection / TransactionPairCollection / ITotalsManager есть и в
// пространстве проводок документа, и в пространстве итогов.
using ITotalsManager = ZuloOne.Managers.ITotalsManager;
using TransactionCollection = ZuloOne.Totals.TransactionCollection;
using TransactionPairCollection = ZuloOne.Totals.TransactionPairCollection;

// ═══ ДРАЙВЕР ВЫБЫТИЯ СЕБЕСТОИМОСТИ ═══════════════════════════════════════════
//
// Расходную ногу себестоимости порождает НЕ документ, а движение склада: драйвер
// висит на регистре Stock, видит ВЕСЬ набор проводок проводимого документа
// (платформа зовёт ValidateTransactions каждому драйверу цепочки, отдавая ему
// полный набор), а затем — уже после того, как движения записаны, — списывает
// себестоимость выбывшего количества.
//
// ПОЧЕМУ НЕ ПРОВОДКИ НА ДОКУМЕНТАХ. Списывать себестоимость обязаны продажа,
// списание, отпуск в производство, отбор со склада и любой будущий расходный
// документ. Ножка в каждом транзакционном скрипте — это N копий одного правила,
// которые разъезжаются: достаточно завести документ и забыть про себестоимость.
// Правило одно и живёт в одном месте: УМЕНЬШИЛСЯ СКЛАДСКОЙ ОСТАТОК — списалась
// себестоимость. Модель Costing из-за этого не заводит ни документов, ни
// проводок, ни сервисов: у неё регистры, настройка и два драйвера.
//
// ЧИСТОЕ количество по товару, а не отдельные проводки. Перемещение между
// ячейками — это ДВА движения Stock по одному товару (−24 из FromCell, +24 в
// ToCell). Товар не выбыл: он переехал. Считай драйвер каждую отрицательную
// проводку выбытием — перемещение списывало бы себестоимость, и оценка запаса
// падала бы при переносе коробки с полки на полку. Поэтому движения
// СХЛОПЫВАЮТСЯ по товару в пределах документа, и списывается только чистый
// минус. Производственный заказ (−компоненты, +изделие) схлопывается по РАЗНЫМ
// товарам и потому списывает ровно компоненты.
//
// КОЛИЧЕСТВО УЖЕ НОРМАЛИЗОВАНО. В Stock все транзакционные скрипты пишут
// BaseQuantity (базовую единицу товара) — драйвер читает регистр, а не строки
// документа, и потому не может перепутать «5 ящиков» со «60 штуками» в принципе.
// Слои себестоимости заведены тем же приходом в той же базовой единице.
//
// СКОЛЬКО списывать. Себестоимость есть только у того количества, приход
// которого её зафиксировал: партии в ItemCostFifo создаёт оприходование заказа
// поставщику. Выбытие количества, которого в партиях нет (тестовые остатки,
// заведённые прямыми движениями регистра, инвентаризационные излишки прошлого,
// выпуск производства), списывать нечем — берётся минимум из выбывшего и
// наличного в партиях. Иначе движок отклонил бы перерасход слоёв и уронил
// проведение документа, который к себестоимости отношения не имеет.
//
// ЧЕМ оценивать — решает не этот драйвер: регистр ItemCostFifo считается своим
// драйвером CostingValuation, и метод (FIFO/AVG) с округлением берутся там из
// CostingSettings. Здесь берётся ФАКТ: сумма, на которую движок уменьшил
// партии, и ровно она списывается из стоимости запасов. Так две величины не
// могут разъехаться по построению — какой бы метод ни выбрали в настройках.
public partial class CostingIssueTotalDriver
{
    private const string StockQuantity = "Qty";
    private const string StockItem = "Item";

    // Чистое движение по товару за документ: минус — выбытие, плюс/ноль — нет.
    private readonly Dictionary<Guid, decimal> _netByItem = new();

    /// <summary>
    /// Платформа отдаёт сюда ВЕСЬ набор проводок документа — всех регистров
    /// цепочки. Берём только свои и складываем по товару. Смотрим одиночные
    /// проводки, а не пары: Stock объявлен регистром ОДИНАРНОЙ записи (остаток =
    /// фактический on-hand, встречной ноги «External» нет), и парная проводка на
    /// него платформой не принимается вовсе.
    /// </summary>
    public override void ValidateTransactions(
        TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        base.ValidateTransactions(transactionPairs, transactions);

        foreach (var tv in transactions)
        {
            if (tv.TotalDescriptor.Guid != TotalID) continue;
            if (tv.IsCoordinateNull(StockItem) || tv.IsValueNull(StockQuantity)) continue;
            var item = tv.GetCoordinate(StockItem);
            _netByItem[item] = (_netByItem.TryGetValue(item, out var acc) ? acc : 0m) + tv.GetValue(StockQuantity);
        }
    }

    /// <summary>
    /// Движения склада уже записаны — списываем себестоимость выбывшего.
    /// Хук синхронный, а списание идёт через менеджеры: это единственная точка
    /// жизненного цикла драйвера ПОСЛЕ записи движений, и соединения регистра в
    /// ней уже нет — обращение к БД отсюда не превращает окружающую транзакцию
    /// в распределённую.
    /// </summary>
    public override void EndDocument(DateTime transactionDate, Guid docId)
    {
        base.EndDocument(transactionDate, docId);

        var issues = _netByItem.Where(kv => kv.Value < 0m).ToList();
        // Экземпляр драйвера живёт одно проведение, но обнуляем явно: EndDocument
        // — публичный хук, и повторный вызов не должен списать себестоимость
        // второй раз.
        _netByItem.Clear();
        if (issues.Count == 0) return;

        WriteOffAsync(issues, transactionDate, docId).GetAwaiter().GetResult();
    }

    private async Task WriteOffAsync(
        List<KeyValuePair<Guid, decimal>> issues, DateTime movementDate, Guid docId)
    {
        var totals = GetService<ITotalsManager>();

        // InventoryValue разрезан ДИНАМИЧЕСКОЙ аналитикой Item (обязательной), а
        // ITotalsManager.PostMovementAsync аналитики не принимает — движение без
        // неё регистр отклонит. Поэтому именно эта проводка идёт через движок
        // регистров, единственный, чья сигнатура их несёт. (Дырка в контракте
        // менеджера, а не в дисциплине: закрывать её — правка платформы.)
        var movements = GetService<IRegisterMovementService>();
        var inventoryValueId = (await GetService<IMetadataService>().GetAllRegistersAsync())
            .First(r => string.Equals(r.Name, "InventoryValue", StringComparison.OrdinalIgnoreCase)).MetaId;

        foreach (var (item, net) in issues)
        {
            var key = new Dictionary<string, object?> { ["Item"] = item };

            var onHand = await totals.GetBalanceAsync("ItemCostFifo", "Quantity", key);
            var take = Math.Min(-net, onHand);
            if (take <= 0m) continue;

            var valueBefore = await totals.GetBalanceAsync("ItemCostFifo", "Amount", key);

            // Расход по партиям: Amount движок ЗАМЕНИТ себестоимостью, которую
            // посчитает драйвер регистра (FIFO или AVG — по настройке), поэтому
            // здесь он ноль.
            await totals.PostMovementAsync("ItemCostFifo", docId, movementDate, key,
                new Dictionary<string, decimal> { ["Quantity"] = -take, ["Amount"] = 0m });

            var cost = valueBefore - await totals.GetBalanceAsync("ItemCostFifo", "Amount", key);

            await movements.PostMovementAsync(
                inventoryValueId, docId, movementDate,
                new Dictionary<string, object?>(),
                new Dictionary<string, decimal> { ["Qty"] = -take, ["Value"] = -cost },
                analytics: new Dictionary<string, object?> { ["Item"] = item });
        }
    }
}
