public partial class TimeSheetTransactionsScript
{
    // Табель не двигает регистры: он только фиксирует часы. Начисление ФОТ
    // делает AccruePayrollCommand — создаёт PayrollAccrual, у того свои проводки.
    protected override void GetTransactions(TimeSheet document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
    }
}
