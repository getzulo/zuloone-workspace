#nullable enable

// Posts each journal line into the general ledger as a one-sided movement. The
// double-entry INVARIANT (ΣDebit = ΣCredit) is enforced on the document
// (JournalEntryEventHandler.OnBeforePostAsync) — so the ledger, summed over any
// posted set, is always balanced. GL analytics are dynamic (Account / LegalEntity /
// FiscalPeriod), so movements are built with RegisterMovementSpec, not a typed row.
public partial class GLPostingTx
{
    protected override void GetTransactions(JournalEntry document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("GL")
                .An("Account", line.Account)
                .An("LegalEntity", document.LegalEntity)
                .An("FiscalPeriod", document.FiscalPeriod)
                .Res("Debit", line.Debit)
                .Res("Credit", line.Credit));
        }
    }
}
