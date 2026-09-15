#nullable enable

// A vendor payment settles payables: each line −Amount by supplier. The purchase
// order is NOT touched — it stays received, stock and the accrued debt stay put.
// That is why payment is a separate document, not an order subtype: a subtype
// change lifts the previous state's movements and would zero out the stock
// receipt along with the debt (the same lesson recorded in MarkPaidScript on
// the sales side).
public partial class VendorPaymentTx
{
    protected override void GetTransactions(VendorPayment document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("Payable")
                .An(Analytics.Payable.Supplier, line.Supplier)
                .Res("Amount", -line.Amount));
        }
    }
}
