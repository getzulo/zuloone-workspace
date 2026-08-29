public partial class SocialInsuranceAccrualTx
{
    protected override void GetTransactions(SocialInsuranceAccrual document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            // Обе стороны взноса — в одном регистре разными ресурсами: платит их
            // работодатель одним платежом в фонд, но удержанная у работника часть
            // и часть за счёт компании ложатся в разные строки отчётности.
            transactions.Add(new RegisterMovementSpec("SocialInsurance")
                .An(Analytics.SocialInsurance.Employee, line.Employee)
                .An(Analytics.SocialInsurance.Division, document.Division)
                .Res("EmployeeContribution", line.EmployeeContribution)
                .Res("EmployerContribution", line.EmployerContribution));
        }
    }
}
