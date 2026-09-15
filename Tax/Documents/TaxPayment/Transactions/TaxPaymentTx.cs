#nullable enable

// Tax in the registers lives in TaxLedger (accrual) and in GL (liability);
// this document only settles the book account. There is no separate TaxPayable
// register and one must not be introduced here: that would duplicate the ledger
// and the book with a third contour nobody reconciles.
public partial class TaxPaymentTx
{
    protected override void GetTransactions(TaxPayment document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
    }
}
