public partial class PayrollAccrualTx
{
    protected override void GetTransactions(PayrollAccrual document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            // Accrued payroll by division and employee (expense register).
            transactions.Add(new RegisterMovementSpec("Payroll")
                .An(Analytics.Payroll.Division, document.Division)
                .An(Analytics.Payroll.Employee, line.Employee)
                .Res("Amount", line.Amount));

            // Liability to the employee — grows by the accrued amount.
            transactions.Add(new RegisterMovementSpec("PayrollLiability")
                .An(Analytics.PayrollLiability.Employee, line.Employee)
                .Res("Amount", line.Amount));
        }
    }
}
