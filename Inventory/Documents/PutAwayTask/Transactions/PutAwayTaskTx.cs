#nullable enable

// Приёмка → хранение: на строку ДВЕ одиночные проводки Stock (одинарная запись) —
// минус из ячейки приёмки (FromCell, шапка), плюс в ячейку хранения (ToCell, строка).
//
// В регистр уходит BaseQuantity (базовая единица товара, считает платформа при
// сохранении строки); ноль = «единица не указана, пересчёта не было» → введённое
// количество и есть базовое. Обе ноги берут ОДНО значение.
public partial class PutAwayTaskTx
{
    protected override void GetTransactions(PutAwayTask document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            transactions.Add(new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.FromCell).Res("Qty", -qty));
            transactions.Add(new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", line.ToCell).Res("Qty", qty));
        }
    }
}
