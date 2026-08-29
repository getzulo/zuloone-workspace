#nullable enable

// Оплата покупателя гасит дебиторку: по каждой строке −Amount в разрезе клиента.
// Счёт-фактура при этом НЕ трогается — она остаётся выставленной, выручка и
// отгрузка на месте. Именно поэтому оплата вынесена в отдельный документ, а не
// сделана подтипом счёта: смена подтипа снимает движения прошлого состояния и
// обнуляла бы выручку вместе с долгом.
public partial class CustomerPaymentTx
{
    protected override void GetTransactions(CustomerPayment document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("Receivable")
                .An(Analytics.Receivable.Customer, line.Customer)
                .Res("Amount", -line.Amount));
        }
    }
}
