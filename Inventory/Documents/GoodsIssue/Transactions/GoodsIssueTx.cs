#nullable enable

// Реализация/Отгрузка — товар уходит со склада «на продажу». ОДИНОЧНАЯ проводка Stock
// (одинарная запись, как складской регистр в MIQS): минус qty из ячейки отбора
// (FromCell, шапка). Встречной ноги в регистре остатков НЕТ — контрагент продажи
// (выручка/дебиторка) живёт в финансовых регистрах, а не в фиктивной ячейке.
// Защита от отгрузки сверх остатка — в GoodsIssueEventHandler.OnBeforePostAsync.
public partial class GoodsIssueTx
{
    protected override void GetTransactions(GoodsIssue document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
            transactions.Add(
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.FromCell).Res("Qty", -line.Quantity));
    }
}
