#nullable enable

public partial class UaFopEsvAccrualPostedTx
{
    protected override void GetTransactions(
        UaFopEsvAccrual document,
        TransactionPairCollection transactionPairs,
        TransactionCollection transactions)
    {
        if (document.LegalEntity == System.Guid.Empty || document.Amount == 0m)
            return;

        transactions.Add(new RegisterMovementSpec("UaFopEsv")
            .Dim("LegalEntity", document.LegalEntity)
            .Res("Amount", document.Amount));
    }
}
