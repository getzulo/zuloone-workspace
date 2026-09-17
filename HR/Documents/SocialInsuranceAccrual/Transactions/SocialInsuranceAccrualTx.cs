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

            // Удержание: сотруднику причитается нетто, не gross — доля взноса,
            // удержанная в его пользу фондом, уменьшает задолженность перед ним.
            transactions.Add(new RegisterMovementSpec("PayrollLiability")
                .An(Analytics.PayrollLiability.Employee, line.Employee)
                .Res("Amount", -line.EmployeeContribution));
        }
    }
}
