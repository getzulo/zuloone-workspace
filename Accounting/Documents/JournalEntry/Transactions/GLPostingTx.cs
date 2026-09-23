#nullable enable

using System;
using System.Linq;

// Posts each journal line into the general ledger as a one-sided movement. The
// double-entry INVARIANT (ΣDebit = ΣCredit) is enforced on the document
// (JournalEntryEventHandler.OnBeforePostAsync) — so the ledger, summed over any
// posted set, is always balanced. GL analytics are dynamic (Account / LegalEntity /
// FiscalPeriod), so movements are built with RegisterMovementSpec, not a typed row.
//
// Три книги на одной строке: Debit/Credit — финансовая (FIN, всегда),
// Management* — управленческая, Tax* — налоговая. Circuits на шапке говорит,
// какие книги заполнить суммой строки; пусто = FIN,MGT. Так НДС не попадает
// в управленческую книгу, и не нужно плодить вторую проводку.
public partial class GLPostingTx
{
    protected override void GetTransactions(JournalEntry document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var raw = string.IsNullOrWhiteSpace(document.Circuits) ? "FIN,MGT" : document.Circuits;
        var tags = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var mgt = tags.Any(t => string.Equals(t, "MGT", StringComparison.OrdinalIgnoreCase));
        var tax = tags.Any(t => string.Equals(t, "TAX", StringComparison.OrdinalIgnoreCase));

        // Книга ведётся в ФУНКЦИОНАЛЬНОЙ валюте юрлица, а строки введены в
        // валюте документа. Курс проштампован на шапке в OnBeforeSave: сюда
        // сходить за ним нечем — GetTransactions синхронный. Ноль сюда не
        // доезжает, его отклоняет OnBeforePost; единица — обычный случай
        // совпадающих валют.
        var fx = document.ExchangeRate <= 0m ? 1m : document.ExchangeRate;

        foreach (var line in document.Lines)
        {
            var debit = line.Debit * fx;
            var credit = line.Credit * fx;
            transactions.Add(new RegisterMovementSpec("GL")
                .An(Analytics.GL.Account, line.Account)
                .An(Analytics.GL.LegalEntity, document.LegalEntity)
                .An(Analytics.GL.FiscalPeriod, document.FiscalPeriod)
                .Res("Debit", debit)
                .Res("Credit", credit)
                .Res("ManagementDebit", mgt ? debit : 0m)
                .Res("ManagementCredit", mgt ? credit : 0m)
                .Res("TaxDebit", tax ? debit : 0m)
                .Res("TaxCredit", tax ? credit : 0m));
        }
    }
}
