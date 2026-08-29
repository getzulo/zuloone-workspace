public partial class PayrollAccrualTx
{
    protected override void GetTransactions(PayrollAccrual document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            // Начисленный ФОТ по подразделению и сотруднику (затратный регистр).
            transactions.Add(new RegisterMovementSpec("Payroll")
                .An(Analytics.Payroll.Division, document.Division)
                .An(Analytics.Payroll.Employee, line.Employee)
                .Res("Amount", line.Amount));

            // Задолженность перед сотрудником — растёт на сумму начисления.
            transactions.Add(new RegisterMovementSpec("PayrollLiability")
                .An(Analytics.PayrollLiability.Employee, line.Employee)
                .Res("Amount", line.Amount));
        }
    }
}
