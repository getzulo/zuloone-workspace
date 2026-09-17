#nullable enable

// Налог в регистрах живёт в TaxLedger (начисление) и в GL (обязательство);
// этот документ только гасит счёт в книге. Отдельного регистра TaxPayable
// нет и заводить его здесь нельзя: это продублировало бы леджер и книгу
// третьим контуром, который никто не сверяет.
public partial class TaxPaymentTx
{
    protected override void GetTransactions(TaxPayment document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
    }
}
