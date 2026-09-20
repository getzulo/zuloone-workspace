#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
// оплачено (обе базы БЕЗ налога), — и облагается max(Shipped, Paid): больший из
// них и есть «события, которые уже произошли». Accrued помнит, какая часть базы
// уже обложена, так что каждое событие берёт налог ТОЛЬКО с прироста. Отсюда
// само собой выходит верное поведение во всех четырёх случаях: предоплата
// облагается сразу; отгрузка после неё в её пределах не облагается второй раз;
// отгрузка без оплаты облагается сразу; оплата после отгрузки не облагается
// повторно.
//
// ГДЕ ЭТО ЗОВУТ. В событии ПЕРЕД сменой подтипа, а не в проводке: чтение
// остатков асинхронно, а GetTransactions синхронна. Результат штампуется полем
// на документе, и транзакционный скрипт уже просто раскладывает его по
// регистрам — тот же приём, которым в ядро приходят TaxRateApplied и
// NonRecoverableVat.
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
    /// Торговая точка договора. Строка оплаты её не несёт, а регистр требует —
    /// берём с договора, который на точке и висит.
    /// </summary>
    public async Task<Guid> OutletOfAsync(Guid contract)
    {
        if (contract == Guid.Empty) return Guid.Empty;
        var record = await _contracts.GetRecordAsync(contract);
        return record?.Outlet ?? Guid.Empty;
    }

    /// <summary>
    /// Какая часть базы облагается ПДВ ПРЯМО СЕЙЧАС, если к договору добавить
    /// <paramref name="addShipped"/> отгрузки и <paramref name="addPaid"/> оплаты.
    ///
    /// Ноль — законный и частый ответ: отгрузка, целиком закрытая предоплатой,
    /// ничего не добавляет к max(Shipped, Paid), и налог по ней уже начислен.
    /// Отрицательного не возвращает: сторнирование идёт кредит-нотой, у которой
    /// своя проводка, а не отрицательным приростом здесь.
    /// </summary>
    public async Task<decimal> TaxableIncrementAsync(
        Guid customer, Guid outlet, Guid contract, decimal addShipped, decimal addPaid)
    {
        if (contract == Guid.Empty) return 0m;

        var shipped = await BalanceAsync(customer, outlet, contract, "Shipped");
        var paid = await BalanceAsync(customer, outlet, contract, "Paid");
        var accrued = await BalanceAsync(customer, outlet, contract, "Accrued");

        var target = Math.Max(shipped + addShipped, paid + addPaid);
        var increment = target - accrued;
        return increment > 0m ? increment : 0m;
    }

    private Task<decimal> BalanceAsync(Guid customer, Guid outlet, Guid contract, string resource)
        => _totals.GetBalanceAsync("UaVatFirstEvent", resource,
            new Dictionary<string, object?>
            {
                ["Customer"] = customer,
                ["CustomerOutlet"] = outlet,
                ["SalesContract"] = contract,
            });
}
