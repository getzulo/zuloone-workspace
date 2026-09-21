#nullable enable
using ZuloOne.Services.Contracts;

// Перша подія со стороны отгрузки. Скрипт больше НЕ начисляет налог сам — он
// только двигает базу договора, а начисляет драйвер UaVatFirstEvent.
//
// ПОЧЕМУ ТАК. Обязательство рождает не счёт, а то из событий, что случилось
// раньше: отгрузка ИЛИ оплата (ПКУ 187.1). Начисляй налог здесь — и предоплата,
// пришедшая первой, была бы обложена дважды: один раз оплатой, второй раз
// отгрузкой. Правило живёт в одном месте, а документы только сообщают о событии.
//
// ПИШЕМ БАЗУ БЕЗ НАЛОГА. Оплата пишет деньги С налогом, и приводит их к базе
// драйвер: для этого нужна ставка, а она резолвится асинхронно — отсюда нельзя.
// Ставка тут больше не нужна вовсе, поэтому проверка TaxRateApplied ушла: «нет
// контура — нет проводки» решается тем же драйвером, на один уровень позже.
//
// ТОЧКУ НЕ ПИШЕМ: договор принадлежит ровно одной торговой точке, в ключе она
// избыточна. В UaVatPayable точка остаётся — её подставит драйвер.
public partial class UaVatTx
{
    protected override void GetTransactions(SalesRealization document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var pricing = GetService<IPricingService>();

        decimal baseAmount = 0m;
        foreach (var line in document.Lines)
            baseAmount += pricing.LineAmount(line.Quantity, line.UnitPrice, document.DiscountPercent);

        if (baseAmount == 0m) return;

        transactions.Add(new RegisterMovementSpec("UaVatFirstEvent")
            .An(Analytics.UaVatFirstEvent.Customer, document.Customer)
            .An(Analytics.UaVatFirstEvent.SalesContract, document.Contract)
            .Res("Shipped", baseAmount));
    }
}
