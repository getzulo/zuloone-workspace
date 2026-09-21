#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Service "UaFirstEvent": перша подія украинского ПДВ (ПКУ 187.1).
//
// ПРАВИЛО. Обязательство возникает на дату того из двух событий, что случилось
// РАНЬШЕ: отгрузка или получение денег. В отличие от Саудовской Аравии, где
// налог привязан к счёту, здесь предоплата сама по себе рождает обязательство,
// а последующая отгрузка в её пределах — уже нет.
//
// КАК ЭТО СЧИТАЕТСЯ, И ПОЧЕМУ НЕ СОПОСТАВЛЕНИЕМ. Соблазн — связать конкретную
// оплату с конкретной отгрузкой и смотреть, что из них первое. Этого нельзя
// сделать честно: строка CustomerPayment несёт клиента, договор и сумму, но НЕ
// ссылку на заказ или счёт. Сопоставление пришлось бы выдумать, и оно разъехалось
// бы на первой же частичной оплате.
//
// Вместо этого по договору копятся два итога — сколько отгружено и сколько
// оплачено, — и облагается max из них: больший и есть «события, которые уже
// произошли». Accrued помнит, какая часть базы уже обложена, так что каждое
// событие берёт налог ТОЛЬКО с прироста. Отсюда само собой выходит верное
// поведение во всех четырёх случаях: предоплата облагается сразу; отгрузка после
// неё в её пределах не облагается второй раз; отгрузка без оплаты облагается
// сразу; оплата после отгрузки не облагается повторно.
//
// КЛЮЧ — КЛИЕНТ И ДОГОВОР, БЕЗ ТОЧКИ. Договор принадлежит ровно одной торговой
// точке, поэтому точка в ключе избыточна. Практическая сторона той же медали:
// строка оплаты точку не несёт, а транзакционный скрипт синхронен и достать её
// из договора не может. В UaVatPayable точка остаётся — её подставляет драйвер,
// который асинхронен.
//
// ГДЕ ЭТО ЗОВУТ. Из драйвера итогов UaVatFirstEvent, уже ПОСЛЕ записи движений:
// прирост зависит от состояния, состояние читается асинхронно, а
// GetTransactions синхронна. Путь «посчитать в событии и проштамповать полем»
// закрыт — поля расширения не попадают в генерируемый класс документа.
public partial class UaFirstEvent
{
    private readonly ITotalsManager _totals;
    private readonly IDictionaryManager<SalesContract> _contracts;

    public UaFirstEvent(ITotalsManager totals, IDictionaryManager<SalesContract> contracts)
    {
        _totals = totals;
        _contracts = contracts;
    }

    /// <summary>
    /// Торговая точка договора. Нужна не здесь, а на выходе — в UaVatPayable,
    /// где срез по точкам одного клиента обязан не смешиваться.
    /// </summary>
    public async Task<Guid> OutletOfAsync(Guid contract)
    {
        if (contract == Guid.Empty) return Guid.Empty;
        var record = await _contracts.GetRecordAsync(contract);
        return record?.Outlet ?? Guid.Empty;
    }

    /// <summary>
    /// Какая часть базы договора облагается ПДВ ПРЯМО СЕЙЧАС: прирост
    /// max(Shipped, Paid) над уже обложенным. Зовётся ПОСЛЕ записи движений, так
    /// что остатки уже включают текущий документ.
    ///
    /// ВНИМАНИЕ НА АСИММЕТРИЮ. Shipped — база БЕЗ налога (столько стоит товар),
    /// Paid — деньги С налогом (столько пришло на счёт). Сравнивать их напрямую
    /// нельзя: предоплата 120 при ставке 20% закрывает базу 100, а не 120.
    /// Приведение делается здесь, а не в проводке, потому что для него нужна
    /// ставка, а ставка резолвится асинхронно.
    ///
    /// Ноль — законный и частый ответ: отгрузка, целиком закрытая предоплатой,
    /// не двигает max и не добавляет ничего. МИНУС тоже законный: кредит-нота
    /// уменьшает Shipped, цель опускается ниже обложенного, и налог должен
    /// освободиться.
    /// </summary>
    public async Task<decimal> TaxableIncrementAsync(Guid customer, Guid contract, decimal rate)
    {
        if (contract == Guid.Empty) return 0m;

        var shipped = await BalanceAsync(customer, contract, "Shipped");
        var paidGross = await BalanceAsync(customer, contract, "Paid");
        var accrued = await BalanceAsync(customer, contract, "Accrued");

        var scale = GlobalConstants.Get<int?>("AmountScale") ?? 2;
        var paidNet = rate > 0m
            ? Math.Round(paidGross / (1m + rate), scale, MidpointRounding.AwayFromZero)
            : paidGross;

        // ЗНАКОВАЯ величина, и это существенно. Вверх её двигают отгрузка и
        // оплата, вниз — кредит-нота, уменьшающая Shipped. Зажми минус в ноль,
        // как было сначала, и налог по кредит-ноте не освободится, а планка
        // max(Shipped, Paid) останется завышенной: следующая отгрузка по
        // договору не начислит ничего, пока её не перекроет.
        return Math.Max(shipped, paidNet) - accrued;
    }

    private Task<decimal> BalanceAsync(Guid customer, Guid contract, string resource)
        => _totals.GetBalanceAsync("UaVatFirstEvent", resource,
            new Dictionary<string, object?>
            {
                ["Customer"] = customer,
                ["SalesContract"] = contract,
            });
}
