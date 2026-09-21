#nullable enable

// Перша подія со стороны денег. Оплата, пришедшая раньше отгрузки, сама рождает
// обязательство по ПДВ (ПКУ 187.1) — именно этим Украина отличается от режимов,
// где налог привязан к счёту.
//
// Скрипт налог НЕ начисляет: он только сообщает, что по договору пришли деньги.
// Начисляет драйвер UaVatFirstEvent, потому что решение зависит от состояния
// (сколько уже отгружено и сколько уже обложено), а состояние читается
// асинхронно — из синхронного GetTransactions его не достать.
//
// ПИШЕМ БРУТТО, и это не небрежность. Отгрузка кладёт в Shipped базу БЕЗ налога,
// а сюда приходят деньги С налогом: предоплата 120 при ставке 20% закрывает базу
// 100, а не 120. Привести одно к другому может только тот, у кого есть ставка, —
// драйвер. Складывать Shipped и Paid напрямую нельзя нигде.
//
// Точку не пишем, и не только потому, что строка оплаты её не несёт: договор
// принадлежит ровно одной торговой точке, так что в ключе она избыточна.
public partial class UaPaymentFirstEventTx
{
    protected override void GetTransactions(CustomerPayment document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            if (line.Amount == 0m || line.Contract == System.Guid.Empty) continue;

            transactions.Add(new RegisterMovementSpec("UaVatFirstEvent")
                .Dim("Customer", line.Customer)
                .Dim("SalesContract", line.Contract)
                .Res("Paid", line.Amount));
        }
    }
}
