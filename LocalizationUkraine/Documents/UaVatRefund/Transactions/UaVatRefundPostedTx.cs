#nullable enable

public partial class UaVatRefundPostedTx
{
    protected override void GetTransactions(
        UaVatRefund document,
        TransactionPairCollection transactionPairs,
        TransactionCollection transactions)
    {
        if (document.LegalEntity == System.Guid.Empty || document.Amount == 0m)
            return;

        transactions.Add(new RegisterMovementSpec("UaVatRefundClaim")
            .Dim("LegalEntity", document.LegalEntity)
            .Res("Amount", document.Amount));
    }
}
