#nullable enable

// Оплата поставщику гасит кредиторку: по каждой строке −Amount в разрезе
// поставщика. Заказ на покупку при этом НЕ трогается — он остаётся
// оприходованным, склад и начисленный долг на месте. Именно поэтому оплата
// вынесена в отдельный документ, а не сделана подтипом заказа: смена подтипа
// снимает движения прошлого состояния и обнулила бы вместе с долгом ещё и
// приход на склад (тот же урок, что записан в MarkPaidScript на стороне продаж).
public partial class VendorPaymentTx
{
    protected override void GetTransactions(VendorPayment document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("Payable")
                .An(Analytics.Payable.Supplier, line.Supplier)
                .Res("Amount", -line.Amount));
        }
    }
}
