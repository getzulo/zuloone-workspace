#nullable enable

// Инвентаризация проводится в StockCountEventHandler.OnBeforePostAsync: там есть
// доступ к текущему остатку (IRegisterMovementService), которого нет в Tx. Здесь —
// пусто; связка Tx с подтипом Posted нужна, чтобы движок запустил цикл проведения.
public partial class StockCountTx
{
    protected override void GetTransactions(StockCount document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
    }
}
