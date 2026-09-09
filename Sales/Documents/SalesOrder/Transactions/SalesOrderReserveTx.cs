#nullable enable

// Reserve is now created by SalesInvoiceReserveTx when the invoice is set to Reserved.
// This script is intentionally empty — kept for backward compatibility.
public partial class SalesOrderReserveTx
{
    protected override void GetTransactions(SalesOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        // No-op: reservation is handled by SalesInvoiceReserveTx.
    }
}
