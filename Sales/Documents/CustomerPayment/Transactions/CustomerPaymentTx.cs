#nullable enable

// A customer payment settles receivable: −Amount per line by customer.
// The invoice is NOT touched — it stays issued, revenue and shipment stay.
// That is why payment is a separate document, not an invoice subtype: a
// subtype change lifts the previous state's movements and would zero revenue
// together with the debt.
public partial class CustomerPaymentTx
{
    protected override void GetTransactions(CustomerPayment document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("Receivable")
                .An(Analytics.Receivable.Customer, line.Customer)
                .An(Analytics.Receivable.SalesContract, line.Contract)
                .Res("Amount", -line.Amount));
        }
    }
}
