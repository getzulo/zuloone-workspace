#nullable enable

using System;
using System.Linq;

// Posts each journal line into the general ledger as a one-sided movement. The
// double-entry INVARIANT (ΣDebit = ΣCredit) is enforced on the document
// (JournalEntryEventHandler.OnBeforePostAsync) — so the ledger, summed over any
// posted set, is always balanced. GL analytics are dynamic (Account / LegalEntity /
// FiscalPeriod), so movements are built with RegisterMovementSpec, not a typed row.
//
// Three books on one line: Debit/Credit — financial (FIN, always),
// Management* — management, Tax* — tax. Circuits on the header says which
// books to fill with the line amount; empty = FIN,MGT. That way VAT does not
// enter the management book, and a second journal entry is not needed.
public partial class GLPostingTx
{
    protected override void GetTransactions(JournalEntry document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var raw = string.IsNullOrWhiteSpace(document.Circuits) ? "FIN,MGT" : document.Circuits;
        var tags = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var mgt = tags.Any(t => string.Equals(t, "MGT", StringComparison.OrdinalIgnoreCase));
        var tax = tags.Any(t => string.Equals(t, "TAX", StringComparison.OrdinalIgnoreCase));

        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("GL")
                .An(Analytics.GL.Account, line.Account)
                .An(Analytics.GL.LegalEntity, document.LegalEntity)
                .An(Analytics.GL.FiscalPeriod, document.FiscalPeriod)
                .Res("Debit", line.Debit)
                .Res("Credit", line.Credit)
                .Res("ManagementDebit", mgt ? line.Debit : 0m)
                .Res("ManagementCredit", mgt ? line.Credit : 0m)
                .Res("TaxDebit", tax ? line.Debit : 0m)
                .Res("TaxCredit", tax ? line.Credit : 0m));
        }
    }
}
