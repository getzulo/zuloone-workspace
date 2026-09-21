#nullable enable
using ZuloOne.Services.Contracts;

// Возврат поставщику со стороны первого события: он уменьшает БАЗУ, а кредит
// снимает драйвер — так же, как и начисляет.
//
// Зеркало кредит-ноты продажи, и осторожность здесь та же. Минус прямо в
// UaVatCredit дал бы верную цифру налога и сломал бы модель: Received и Credited
// остались бы завышенными, планка max(Received, PaidOut) застряла бы, и
// СЛЕДУЮЩИЙ приход от того же поставщика не дал бы кредита, пока не перекрыл
// застрявший уровень. Ошибка проявилась бы не на возврате, а на документе через
// один — худший вид расхождения.
//
// ДЕНЬГИ НЕ ТРОГАЕМ. Возврат товара без возврата денег не отменяет оплату: если
// перша подія была оплатой и аванс у поставщика остался, кредит сохраняется.
// Симметрично правилу на стороне продаж.
public partial class UaPurchaseCreditTx
{
    protected override void GetTransactions(PurchaseCreditNote document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
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
            .Res("Received", -baseAmount));
    }
}
