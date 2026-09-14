public partial class SocialInsuranceAccrualTx
{
    protected override void GetTransactions(SocialInsuranceAccrual document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            // Both contribution sides live in one register as different resources:
            // the employer pays them in a single remittance to the fund, but the
            // share withheld from the employee and the company-paid share land on
            // different reporting lines.
            transactions.Add(new RegisterMovementSpec("SocialInsurance")
                .An(Analytics.SocialInsurance.Employee, line.Employee)
                .An(Analytics.SocialInsurance.Division, document.Division)
                .Res("EmployeeContribution", line.EmployeeContribution)
                .Res("EmployerContribution", line.EmployerContribution));

            // Withholding: the employee is owed net, not gross — the contribution
            // share withheld for the fund reduces the liability to them.
            transactions.Add(new RegisterMovementSpec("PayrollLiability")
                .An(Analytics.PayrollLiability.Employee, line.Employee)
                .Res("Amount", -line.EmployeeContribution));
        }
    }
}
