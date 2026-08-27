public partial class PayrollAccrualTx
{
    protected override void GetTransactions(PayrollAccrual document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            // Начисленный ФОТ по подразделению и сотруднику (затратный регистр).
            transactions.Add(new RegisterMovementSpec("Payroll")
                .An("Division", document.Division)
                .An("Employee", line.Employee)
                .Res("Amount", line.Amount));

            // Задолженность перед сотрудником — растёт на сумму начисления.
            transactions.Add(new RegisterMovementSpec("PayrollLiability")
                .An("Employee", line.Employee)
                .Res("Amount", line.Amount));
        }
    }
}
