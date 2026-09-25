#nullable enable

// Черновик ещё не списывает Stock. Компоненты этого заказа садятся в
// ReservedStock на ячейке списания, иначе продажа видит их свободными и
// согласует отгрузку. Запуск и выпуск этот скрипт не повторяют: Mix снимает
// черновик, а списание уже уменьшает сам остаток.
public partial class ProductionDraftReserveTx
{
    protected override void GetTransactions(ProductionOrder document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        if (document.Location == Guid.Empty) return;
        foreach (var line in document.Components)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.QtyRequired;
            if (qty <= 0m || line.Component == Guid.Empty) continue;
            transactions.Add(new RegisterMovementSpec("ReservedStock")
                .An("Item", line.Component)
                .An("Cell", document.Location)
                .Res("Qty", qty));
        }
    }
}
