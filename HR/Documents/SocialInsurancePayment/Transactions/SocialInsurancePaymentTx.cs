#nullable enable

// A payment to the social-insurance fund settles the liability: each line subtracts
// both contribution sides — the share withheld from the employee and the employer
// share. The contribution accrual is NOT touched: both the calculation itself and
// the withholding from the employee liability stay posted. That is why the payment
// is a separate document, not an accrual subtype — a subtype change would lift
// the previous state's movements and, along with the fund liability, return the
// withheld amount to the employee (the same lesson as MarkPaidScript on the
// sales side).
//
// The slice is exactly the same as the accrual (Employee + Division) — otherwise
// the minus would land on a different analytics combination and the liability
// would not close.
public partial class SocialInsurancePaymentTx
{
    protected override void GetTransactions(SocialInsurancePayment document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactions.Add(new RegisterMovementSpec("SocialInsurance")
                .An(Analytics.SocialInsurance.Employee, line.Employee)
                .An(Analytics.SocialInsurance.Division, document.Division)
                .Res("EmployeeContribution", -line.EmployeeContribution)
                .Res("EmployerContribution", -line.EmployerContribution));
        }
    }
}
