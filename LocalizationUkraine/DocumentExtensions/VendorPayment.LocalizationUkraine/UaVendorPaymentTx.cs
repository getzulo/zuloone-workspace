#nullable enable

// Перша подія на ВХОДЕ со стороны денег (ПКУ 198.2): оплата поставщику, ушедшая
// раньше поставки, сама рождает право на налоговый кредит.
//
// ПИШЕМ БРУТТО — столько ушло со счёта, вместе с налогом. Получение товара
// кладёт в Received базу БЕЗ налога, поэтому складывать их напрямую нельзя;
// приводит одно к другому драйвер, у которого есть ставка.
//
// ЮРЛИЦО ИЗ ШАПКИ, ПОСТАВЩИК ИЗ СТРОКИ — так устроен документ. И здесь, в
// отличие от оплаты покупателя, сверять юрлицо НЕ С ЧЕМ: договоров поставки в
// Purchasing нет, а поставщик может продавать нескольким нашим юрлицам сразу.
// Ошибись оператор юрлицом — Received и PaidOut разойдутся по координатам и
// кредит будет взят дважды. Это известное ограничение, а не недосмотр: закрыть
// его может только появление договора поставки.
public partial class UaVendorPaymentTx
{
    protected override void GetTransactions(VendorPayment document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            if (line.Amount == 0m || line.Supplier == System.Guid.Empty) continue;

            transactions.Add(new RegisterMovementSpec("UaPurchaseFirstEvent")
                .Dim("LegalEntity", document.LegalEntity)
                .Dim("Supplier", line.Supplier)
                .Res("PaidOut", line.Amount));
        }
    }
}
