public partial class LoyaltyRedemptionTx
{
    protected override void GetTransactions(LoyaltyRedemption document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        // Списание баллов клиента. Регистр LoyaltyPoints не допускает
        // отрицательный остаток → списать больше накопленного нельзя.
        transactions.Add(new RegisterMovementSpec("LoyaltyPoints")
            .Dim("Customer", document.Customer)
            .Res("Points", -document.Points));
    }
}
