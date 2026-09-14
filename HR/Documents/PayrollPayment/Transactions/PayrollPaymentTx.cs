public partial class PayrollPaymentTx
{
    protected override void GetTransactions(PayrollPayment document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            // Payment settles the liability to the employee.
            // PayrollLiability forbids a negative balance —
            // overpayment (paying more than accrued) will be rejected by the engine.
            transactions.Add(new RegisterMovementSpec("PayrollLiability")
                .An(Analytics.PayrollLiability.Employee, line.Employee)
                .Res("Amount", -line.Amount));
        }
    }
}
