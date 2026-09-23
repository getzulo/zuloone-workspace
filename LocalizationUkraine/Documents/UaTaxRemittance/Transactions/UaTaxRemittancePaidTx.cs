#nullable enable

// Перерахування до бюджету пише Transferred у UaPayrollLevy.
// TaxLedger не чіпаємо: він уже має нараховане при PayrollAccrual.
// Mix на Voided зніме ці рухи сам — на відміну від виплати ФОТ, де PaidBase
// пише подієвий обробник і знімати треба руками.
public partial class UaTaxRemittancePaidTx
{
    protected override void GetTransactions(
        UaTaxRemittance document,
        TransactionPairCollection transactionPairs,
        TransactionCollection transactions)
    {
        if (document.LegalEntity == System.Guid.Empty || document.TaxCode == System.Guid.Empty)
            return;

        foreach (var line in document.Lines)
        {
            if (line.Employee == System.Guid.Empty || line.Amount == 0m) continue;

            transactions.Add(new RegisterMovementSpec("UaPayrollLevy")
                .Dim("LegalEntity", document.LegalEntity)
                .Dim("Employee", line.Employee)
                .Dim("TaxCode", document.TaxCode)
                .Res("Transferred", line.Amount));
        }
    }
}
