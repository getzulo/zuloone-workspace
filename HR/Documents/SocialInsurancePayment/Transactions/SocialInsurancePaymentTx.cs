#nullable enable

// Платёж в фонд соцстраха гасит обязательство: по каждой строке минусом обе
// стороны взноса — и удержанная у работника доля, и доля работодателя. Начисление
// взносов при этом НЕ трогается: и сам расчёт, и удержание из задолженности перед
// сотрудником остаются проведёнными. Именно поэтому платёж вынесен в отдельный
// документ, а не сделан подтипом начисления — смена подтипа сняла бы движения
// прошлого состояния и вместе с обязательством перед фондом вернула бы работнику
// удержанное (тот же урок, что в MarkPaidScript на стороне продаж).
//
// Разрез строго тот же, что у начисления (Сотрудник + Подразделение) — иначе
// минус лёг бы в другую комбинацию аналитик и обязательство не закрылось бы.
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
