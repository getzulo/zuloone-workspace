#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;
// ZuloOne.Managers и ZuloOne.Totals целиком не открываются: имена
// TransactionCollection / TransactionPairCollection / ITotalsManager есть и в
// пространстве проводок документа, и в пространстве итогов.
using ITotalsManager = ZuloOne.Managers.ITotalsManager;
using TransactionCollection = ZuloOne.Totals.TransactionCollection;
using TransactionPairCollection = ZuloOne.Totals.TransactionPairCollection;

// ═══ ДРАЙВЕР ПЕРШОЇ ПОДІЇ ════════════════════════════════════════════════════
//
// Начисление ПДВ порождает НЕ документ, а сдвиг базы по договору. Драйвер висит
// на UaVatFirstEvent, видит движения проводимого документа и — уже после того,
// как они записаны, — начисляет налог на прирост.
//
// ПОЧЕМУ НЕ ПРОВОДКИ НА ДОКУМЕНТАХ. Первое событие рождают и отгрузка, и оплата,
// завтра добавится возврат аванса. Ножка в каждом транзакционном скрипте — это N
// копий одного правила, которые разъезжаются: достаточно завести документ и
// забыть про налог. Правило одно: СДВИНУЛАСЬ база по договору — начислился ПДВ
// на прирост. Ровно тем же соображением живёт драйвер себестоимости в Costing.
//
// И ПОЧЕМУ ЭТО НЕЛЬЗЯ СДЕЛАТЬ В ПРОВОДКЕ, даже захотев. Прирост зависит от
// СОСТОЯНИЯ (сколько уже обложено), состояние читается асинхронно, а
// GetTransactions синхронна. Обходной путь «посчитать в событии и проштамповать
// полем» тоже закрыт: поля расширения не попадают в генерируемый класс
// документа, так что ни записать, ни прочитать их по имени скрипт не может.
// Остаётся эта точка — и она же единственно верная по смыслу.
//
// ЧИТАЕМ ОСТАТКИ ПОСЛЕ ЗАПИСИ. В EndDocument движения уже в регистре, поэтому
// Shipped и Paid читаются с учётом текущего документа, и прибавлять дельты
// вручную не нужно. Accrued помнит обложенное ранее — разница и есть налоговая
// база этого события. Отрицательной она не бывает: отгрузка, закрытая
// предоплатой, не двигает max(Shipped, Paid) и даёт ровно ноль.
public partial class UaVatFirstEventTotalDriver
{
    // КЛЮЧ РЕГИСТРА — ИЗМЕРЕНИЯ, И ЭТО НЕ ВКУСОВЩИНА. TransactionBase знает только
    // coordinates; аналитик у него нет вовсе. Объяви клиента и договор аналитиками —
    // и IsCoordinateNull отбросит КАЖДУЮ проводку, драйвер отработает вхолостую, а
    // в логе не будет ни ошибки, ни подсказки: движения записаны, налог не начислен.
    // Ровно на это ушёл отдельный круг отладки.
    private const string Shipped = "Shipped";
    private const string Paid = "Paid";
    private const string Customer = "Customer";
    private const string Outlet = "CustomerOutlet";
    private const string Contract = "SalesContract";

    // Договоры, которых коснулся документ. Значения не копим: остатки всё равно
    // читаются из регистра, здесь важен только САМ ФАКТ, что договор задет.
    private readonly HashSet<(Guid Customer, Guid Contract)> _touched = new();

    /// <summary>
    /// Платформа отдаёт сюда ВЕСЬ набор проводок документа — всех регистров
    /// цепочки. Берём только свои и запоминаем затронутые договоры.
    /// </summary>
    public override void ValidateTransactions(
        TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        base.ValidateTransactions(transactionPairs, transactions);

        foreach (var tv in transactions)
        {
            if (tv.TotalDescriptor.Guid != TotalID) continue;
            if (tv.IsCoordinateNull(Contract) || tv.IsCoordinateNull(Customer)) continue;
            if (tv.IsValueNull(Shipped) && tv.IsValueNull(Paid)) continue;

            _touched.Add((tv.GetCoordinate(Customer), tv.GetCoordinate(Contract)));
        }
    }

    /// <summary>
    /// Движения базы уже записаны — начисляем налог. Хук синхронный, а расчёт
    /// идёт через менеджеры: это единственная точка жизненного цикла драйвера
    /// ПОСЛЕ записи движений, и соединения регистра в ней уже нет, так что
    /// обращение к БД отсюда не превращает окружающую транзакцию в
    /// распределённую.
    /// </summary>
    public override void EndDocument(DateTime transactionDate, Guid docId)
    {
        base.EndDocument(transactionDate, docId);

        var contracts = _touched.ToList();
        // Экземпляр драйвера живёт одно проведение, но обнуляем явно: EndDocument
        // — публичный хук, и повторный вызов не должен начислить налог дважды.
        _touched.Clear();
        if (contracts.Count == 0) return;

        AccrueAsync(contracts, transactionDate, docId).GetAwaiter().GetResult();
    }

    private async Task AccrueAsync(
        List<(Guid Customer, Guid Contract)> contracts, DateTime movementDate, Guid docId)
    {
        var rate = await RateOnAsync(movementDate);
        if (rate <= 0m) return; // контур не настроен — молчим, как и прежде

        var firstEvent = GetService<IUaFirstEvent>();
        var tax = GetService<ITaxService>();

        // Оба регистра разрезаны ДИНАМИЧЕСКИМИ аналитиками, а
        // ITotalsManager.PostMovementAsync аналитики не принимает — движение без
        // них регистр отклонит. Поэтому идём через движок регистров,
        // единственный, чья сигнатура их несёт.
        var movements = GetService<IRegisterMovementService>();
        var registers = await GetService<IMetadataService>().GetAllRegistersAsync();
        var firstEventId = registers.First(r =>
            string.Equals(r.Name, "UaVatFirstEvent", StringComparison.OrdinalIgnoreCase)).MetaId;
        var payableId = registers.First(r =>
            string.Equals(r.Name, "UaVatPayable", StringComparison.OrdinalIgnoreCase)).MetaId;

        foreach (var key in contracts)
        {
            var taxable = await firstEvent.TaxableIncrementAsync(key.Customer, key.Contract, rate);
            if (taxable <= 0m) continue;

            var vat = tax.CalculateTax(taxable, rate);
            if (vat <= 0m) continue;

            await movements.PostMovementAsync(
                firstEventId, docId, movementDate,
                new Dictionary<string, object?>
                {
                    [Customer] = key.Customer,
                    [Contract] = key.Contract,
                },
                new Dictionary<string, decimal> { ["Accrued"] = taxable });

            // Точка значима только на выходе — в UaVatPayable срез по торговым
            // точкам одного клиента обязан не смешиваться.
            await movements.PostMovementAsync(
                payableId, docId, movementDate,
                new Dictionary<string, object?>(),
                new Dictionary<string, decimal> { ["Amount"] = vat },
                analytics: new Dictionary<string, object?>
                {
                    [Customer] = key.Customer,
                    [Outlet] = await firstEvent.OutletOfAsync(key.Contract),
                    [Contract] = key.Contract,
                });
        }
    }

    /// <summary>
    /// Ставка на дату движения по НАСТРОЕННОМУ коду по умолчанию. Документа здесь
    /// нет — у драйвера только координаты регистра, — поэтому ставка берётся из
    /// контура, а не с документа. Даты совпадают: движение пишется датой
    /// документа, а значит задним числом посчитается историческая ставка.
    /// </summary>
    private async Task<decimal> RateOnAsync(DateTime date)
    {
        var tax = GetService<ITaxService>();
        var code = await tax.ResolveDefaultTaxCodeAsync();
        if (code is not Guid codeId) return 0m;
        return await tax.ResolveRateAsync(codeId, date) ?? 0m;
    }
}
