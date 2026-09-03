public partial class TaxReturnTransactionsScript
{
    // Декларация — снимок оборотов TaxLedger, не источник проводок.
    // Подтип Filed только для чтения; книга уже получила налог из TaxCalculation.
    protected override void GetTransactions(TaxReturn document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
    }
}
