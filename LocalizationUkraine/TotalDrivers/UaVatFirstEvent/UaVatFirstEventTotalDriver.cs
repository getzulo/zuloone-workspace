#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;
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
//
// ═══ РЕЖИМЫ ══════════════════════════════════════════════════════════════════
//
// В ОДНОЙ СИСТЕМЕ ЖИВУТ ТОВ И НЕСКОЛЬКО ФОП, и считаются они по-разному. Режим —
// свойство ЮРЛИЦА, поэтому юрлицо стало измерением регистра: драйвер читает
// только координаты, аналитик TransactionBase не знает вовсе.
//
//   Платник ПДВ (умолчание)   ПДВ с max(Shipped, Paid). ЄП нет.
//   Спрощенець 3% з ПДВ       ОБА: ПДВ с max(...) и ЄП 3% с дохода без ПДВ.
//   Спрощенець 5% без ПДВ     Только ЄП 5% со ВСЕЙ полученной суммы. ПДВ нет.
//   Загальна без ПДВ          Ни того, ни другого: налог на доход считается
//                             отдельными документами по итогам периода.
//
// НОВЫХ РЕГИСТРОВ ПОД ЄП НЕ ЗАВОДИЛОСЬ. Paid уже И ЕСТЬ кассовый оборот, с
// которого платит спрощенець; не хватало только счётчика обложенного — EpAccrued.
// Сам налог уходит в ЧУЖОЙ Tax.TaxLedger, а не в UaVatPayable: леджер уже разрезан
// как надо (код налога × направление × юрлицо), а UaVatPayable разрезан по
// клиентам и торговым точкам — для налога с оборота это бессмысленная нарезка.
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
    private const string Entity = "LegalEntity";

    // Договоры, которых коснулся документ. Значения не копим: остатки всё равно
    // читаются из регистра, здесь важен только САМ ФАКТ, что договор задет.
    private readonly HashSet<(Guid Entity, Guid Customer, Guid Contract)> _touched = new();

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

            // Юрлицо НЕ проверяем на пустоту, в отличие от клиента и договора.
            // Пустое — законная координата: документ без проставленного продавца
            // должен вести себя ровно так, как вёл себя пакет до появления
            // режимов, то есть начислять ПДВ. Отбрось мы такую проводку — старые
            // документы молча перестали бы облагаться.
            var entity = tv.IsCoordinateNull(Entity) ? Guid.Empty : tv.GetCoordinate(Entity);
            _touched.Add((entity, tv.GetCoordinate(Customer), tv.GetCoordinate(Contract)));
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
        List<(Guid Entity, Guid Customer, Guid Contract)> contracts,
        DateTime movementDate,
        Guid docId)
    {
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
        var ledgerId = registers.First(r =>
            string.Equals(r.Name, "TaxLedger", StringComparison.OrdinalIgnoreCase)).MetaId;

        var vatRate = await RateOnAsync(movementDate);

        foreach (var key in contracts)
        {
            var regime = await firstEvent.RegimeOfAsync(key.Entity);

            if (firstEvent.ChargesVat(regime))
                await AccrueVatAsync(
                    firstEvent, tax, movements, firstEventId, payableId,
                    key, vatRate, movementDate, docId);

            await AccrueSingleTaxAsync(
                firstEvent, tax, movements, firstEventId, ledgerId,
                key, regime, vatRate, movementDate, docId);
        }
    }

    /// <summary>
    /// ПДВ по первому событию — то, ради чего регистр и заводился. Ставка 0
    /// означает «контур не настроен»: молчим, как и прежде.
    /// </summary>
    private async Task AccrueVatAsync(
        IUaFirstEvent firstEvent,
        ITaxService tax,
        IRegisterMovementService movements,
        Guid firstEventId,
        Guid payableId,
        (Guid Entity, Guid Customer, Guid Contract) key,
        decimal vatRate,
        DateTime movementDate,
        Guid docId)
    {
        if (vatRate <= 0m) return;

        var taxable = await firstEvent.TaxableIncrementAsync(
            key.Entity, key.Customer, key.Contract, vatRate);
        // Знак не трогаем: плюс — начисление, минус — освобождение по
        // кредит-ноте. Ноль означает «событие не первое» и пишется не будет.
        if (taxable == 0m) return;

        var vat = tax.CalculateTax(Math.Abs(taxable), vatRate);
        if (vat == 0m) return;
        if (taxable < 0m) vat = -vat;

        await movements.PostMovementAsync(
            firstEventId, docId, movementDate,
            Coordinates(key),
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

    /// <summary>
    /// Єдиний податок — КАССОВЫЙ: его базу двигают только деньги, и никакого
    /// max(Shipped, Paid) здесь нет. Отгрузка и кредит-нота проходят мимо, и это
    /// не упущение: возврат товара без возврата денег дохода не отменяет.
    ///
    /// Уходит в Tax.TaxLedger, потому что леджер уже разрезан кодом налога,
    /// направлением и юрлицом — ровно тем, чем декларируется ЄП. UaVatPayable
    /// разрезан клиентами и точками и для налога с оборота не годится.
    /// </summary>
    private async Task AccrueSingleTaxAsync(
        IUaFirstEvent firstEvent,
        ITaxService tax,
        IRegisterMovementService movements,
        Guid firstEventId,
        Guid ledgerId,
        (Guid Entity, Guid Customer, Guid Contract) key,
        UaTaxRegime regime,
        decimal vatRate,
        DateTime movementDate,
        Guid docId)
    {
        var codeId = await firstEvent.SingleTaxCodeIdAsync(regime);
        if (codeId is not Guid single) return; // режим не платит ЄП, либо код не заведён

        var epRate = await tax.ResolveRateAsync(single, movementDate) ?? 0m;
        if (epRate <= 0m) return;

        // У спрощенця 3% деньги приходят С ПДВ, и налог берётся с дохода БЕЗ
        // него; у пятипроцентника ПДВ в деньгах нет — база вся сумма целиком.
        var netOf = firstEvent.ChargesVat(regime) ? vatRate : 0m;
        var epBase = await firstEvent.SingleTaxIncrementAsync(
            key.Entity, key.Customer, key.Contract, netOf);
        if (epBase <= 0m) return;

        var epAmount = tax.CalculateTax(epBase, epRate);
        if (epAmount == 0m) return;

        await movements.PostMovementAsync(
            firstEventId, docId, movementDate,
            Coordinates(key),
            new Dictionary<string, decimal> { ["EpAccrued"] = epBase });

        await movements.PostMovementAsync(
            ledgerId, docId, movementDate,
            new Dictionary<string, object?>(),
            new Dictionary<string, decimal>
            {
                ["TaxBase"] = epBase,
                ["TaxAmount"] = epAmount,
            },
            analytics: new Dictionary<string, object?>
            {
                ["TaxCode"] = single,
                ["TaxDirection"] = await DirectionAsync(),
                ["LegalEntity"] = key.Entity,
            });
    }

    private static Dictionary<string, object?> Coordinates(
        (Guid Entity, Guid Customer, Guid Contract) key)
        => new()
        {
            [Entity] = key.Entity,
            [Customer] = key.Customer,
            [Contract] = key.Contract,
        };

    /// <summary>
    /// Направление для леджера. OUTPUT: єдиний податок — обязательство перед
    /// бюджетом по обороту, та же сторона, что и исходящий ПДВ, и отчёты,
    /// фильтрующие леджер по OUTPUT, обязаны его видеть.
    /// </summary>
    private async Task<Guid> DirectionAsync()
    {
        var rows = await GetService<IDictionaryManager>()
            .GetRecordsAsync<TaxDirection>("Code = 'OUTPUT'", take: 1);
        return rows.Count > 0 ? rows[0].MetaId : Guid.Empty;
    }

    /// <summary>
    /// Ставка ПДВ на дату движения по НАСТРОЕННОМУ коду по умолчанию. Документа
    /// здесь нет — у драйвера только координаты регистра, — поэтому ставка
    /// берётся из контура, а не с документа. Даты совпадают: движение пишется
    /// датой документа, а значит задним числом посчитается историческая ставка.
    /// </summary>
    private async Task<decimal> RateOnAsync(DateTime date)
    {
        var tax = GetService<ITaxService>();
        var code = await tax.ResolveDefaultTaxCodeAsync();
        if (code is not Guid codeId) return 0m;
        return await tax.ResolveRateAsync(codeId, date) ?? 0m;
    }
}
