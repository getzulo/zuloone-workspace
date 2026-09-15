public partial class TaxReturnTransactionsScript
{
    // A return is a snapshot of TaxLedger turnovers, not a source of postings.
    // The Filed subtype is read-only; the book already received the tax from TaxCalculation.
    protected override void GetTransactions(TaxReturn document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
    }
}
