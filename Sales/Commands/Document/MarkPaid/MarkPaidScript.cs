// ОТКЛЮЧЕНА (isEnabled: false), оставлена как документированный тупик.
//
// Идея «оплата = подтип счёта» неверна: при переходе Выставлен → Оплачен движок
// снимает движения ПРОШЛОГО состояния, поэтому вместе с дебиторкой обнулялась и
// ВЫРУЧКА — оплата отменяла продажу. Поймано тестом ReceivableFlowTest
// («выручка сохраняется после оплаты, факт 0.00»).
//
// Правильная модель — отдельный документ CustomerPayment (как выплата в HR):
// счёт остаётся выставленным, а платёж гасит долг своей проводкой.
// Удалить объект из базы нельзя (нет DELETE для команд документа), поэтому
// команда выключена.
public partial class MarkPaidCommand
{
    public override async Task ExecuteAsync(SalesInvoice document, CommandContext context)
    {
        context.AddClientAction(ClientAction.Message(
            "Команда отключена: оплату проводите документом «Оплата покупателя» (CustomerPayment)."));
        await Task.CompletedTask;
    }
}
