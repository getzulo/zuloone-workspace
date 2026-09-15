public partial class PayrollPaymentTx
{
    protected override void GetTransactions(PayrollPayment document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            // Выплата гасит задолженность перед сотрудником.
            // Регистр PayrollLiability запрещает отрицательный остаток —
            // переплата (выплата больше начисленного) будет отклонена движком.
            transactions.Add(new RegisterMovementSpec("PayrollLiability")
                .An(Analytics.PayrollLiability.Employee, line.Employee)
                .Res("Amount", -line.Amount));
        }
    }
}
