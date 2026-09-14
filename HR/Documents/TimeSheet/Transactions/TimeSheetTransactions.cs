public partial class TimeSheetTransactionsScript
{
    // The timesheet does not move registers: it only records hours. Payroll is
    // accrued by AccruePayrollCommand — it creates PayrollAccrual, which has its
    // own postings.
    protected override void GetTransactions(TimeSheet document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
    }
}
