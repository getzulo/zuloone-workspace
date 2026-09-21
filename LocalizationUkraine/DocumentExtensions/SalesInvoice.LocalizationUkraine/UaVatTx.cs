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
// КЛЮЧ — ИЗМЕРЕНИЯ, НЕ АНАЛИТИКИ. Драйвер итогов читает только координаты
// проводки; аналитик TransactionBase не знает вовсе. Ключ, объявленный
// аналитикой, драйверу невидим — движение прошло бы, а начисления не случилось.
//
// ТОЧКУ НЕ ПИШЕМ: договор принадлежит ровно одной торговой точке, в ключе она
// избыточна. В UaVatPayable точка остаётся — её подставит драйвер.
//
// А ВОТ ЮРЛИЦО ПИШЕМ, и по обратной причине: оно НЕ выводится из договора
// однозначно. Продавца счёту проставляет обработчик — сперва из договора, а если
// там пусто, то по ячейке отгрузки (Cell → Zone → Store → Division → LegalEntity);
// вручную вбитое значение не переписывается, потому что ячейка отгрузки и
// продавец совпадают не всегда. Режим налогообложения — свойство юрлица, так что
// координата обязана приехать с документа, а не быть додумана.
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
            .Dim("LegalEntity", document.LegalEntity)
            .Dim("Customer", document.Customer)
            .Dim("SalesContract", document.Contract)
            .Res("Shipped", baseAmount));
    }
}
