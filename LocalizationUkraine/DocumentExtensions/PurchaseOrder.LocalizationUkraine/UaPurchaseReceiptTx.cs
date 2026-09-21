#nullable enable
using ZuloOne.Services.Contracts;

// Перша подія на ВХОДЕ со стороны товара (ПКУ 198.2). Зеркало отгрузки: право
// на налоговый кредит возникает на дату того из событий, что случилось РАНЬШЕ —
// списание денег поставщику или получение товара.
//
// Скрипт налог НЕ считает, а только сообщает о событии: сколько получено. Зачёт
// делает драйвер UaPurchaseFirstEvent, потому что решение зависит от СОСТОЯНИЯ
// (сколько уже оплачено и сколько уже зачтено), состояние читается асинхронно, а
// GetTransactions синхронна.
//
// ПИШЕМ БАЗУ БЕЗ НАЛОГА: товар оценивается без него, а деньги поставщику уходят
// с налогом. Приводит их друг к другу драйвер — для этого нужна ставка.
//
// ЮРЛИЦО БЕРЁМ С ДОКУМЕНТА, А НЕ ИЗ ЯЧЕЙКИ. Оно проштамповано обработчиком на
// сохранении ровно для этого: пройти цепочку ячейка -> зона -> склад ->
// подразделение -> юрлицо синхронный скрипт не может, а режим налогообложения —
// свойство юрлица, и без него драйвер не знает, положен ли кредит вообще.
public partial class UaPurchaseReceiptTx
{
    protected override void GetTransactions(PurchaseOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        if (document.Supplier == System.Guid.Empty) return;

        var pricing = GetService<IPricingService>();

        decimal baseAmount = 0m;
        foreach (var line in document.Lines)
            baseAmount += pricing.LineAmount(line.Quantity, line.UnitPrice);

        if (baseAmount == 0m) return;

        transactions.Add(new RegisterMovementSpec("UaPurchaseFirstEvent")
            .Dim("LegalEntity", document.LegalEntity)
            .Dim("Supplier", document.Supplier)
            .Res("Received", baseAmount));
    }
}
