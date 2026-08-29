#nullable enable

// Реализация/Отгрузка — товар уходит со склада «на продажу». ОДИНОЧНАЯ проводка Stock
// (одинарная запись, как складской регистр в MIQS): минус qty из ячейки отбора
// (FromCell, шапка). Встречной ноги в регистре остатков НЕТ — контрагент продажи
// (выручка/дебиторка) живёт в финансовых регистрах, а не в фиктивной ячейке.
// Защита от отгрузки сверх остатка — в GoodsIssueEventHandler.OnBeforePostAsync.
//
// В регистр уходит BaseQuantity — количество в БАЗОВОЙ единице товара, которое
// платформа считает при сохранении строки из пары (Quantity, Unit). Остаток
// нельзя копить в разных единицах: 2 ящика и 24 штуки сложились бы в 26. Ноль
// здесь значит «единица строки не указана, пересчёта не было» — платформа такую
// строку намеренно пропускает, и введённое количество И ЕСТЬ базовое. Та же пара
// строк стоит во всех складских проводках; своего округления они больше не
// делают — значение приходит округлённым по точности самой единицы.
public partial class GoodsIssueTx
{
    protected override void GetTransactions(GoodsIssue document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            transactions.Add(
                new RegisterMovementSpec("Stock").Dim("Item", line.Item).Dim("Cell", document.FromCell).Res("Qty", -qty));
        }
    }
}
