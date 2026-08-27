#nullable enable

// Posts each calculation line into the tax ledger — the source of tax returns.
// One-sided movement per line (base + amount) by tax code, direction, legal entity.
// (canonical transaction-script shape: no namespace, no base, unqualified types)
public partial class TaxLedgerTx
{
    protected override void GetTransactions(TaxCalculation document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("TaxLedger")
                .An("TaxCode", line.TaxCode)
                .An("TaxDirection", line.Direction)
                .An("LegalEntity", document.LegalEntity)
                .Res("TaxBase", line.TaxBase)
                .Res("TaxAmount", line.TaxAmount));
        }
    }
}
