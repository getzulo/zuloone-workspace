public partial class LoyaltyRedemptionTx
{
    protected override void GetTransactions(LoyaltyRedemption document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        // Customer point redemption. The LoyaltyPoints register does not allow
        // a negative balance → you cannot redeem more than accumulated.
        transactions.Add(new RegisterMovementSpec("LoyaltyPoints")
            .Dim("Customer", document.Customer)
            .Res("Points", -document.Points));
    }
}
