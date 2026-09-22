using ZuloOne.Services.Contracts;

// Кнопка «Запитати оператора про накладну» на конверті, що завис у Issued.
//
// ЧОМУ КНОПКА «СПИТАТИ», А НЕ «НАДІСЛАТИ ЩЕ РАЗ». Стан «відправлено, відповіді
// немає» (InDoubt у черзі) означає, що запит пішов у дріт — накладна могла вже
// зареєструватися. Сліпий повтор у цьому стані дає ДРУГУ реєстрацію тієї самої
// накладної, а це штраф, а не незручність. Саме тому платформа відмовляє в
// retry для InDoubt, і саме тому тут питання, а не подача.
//
// Завдання PullUaTaxInvoiceReceipts робить те саме автоматично. Кнопка потрібна
// тому, що строк реєстрації обмежений законом: бухгалтер не має чекати
// наступного тику, коли йому треба знати ЗАРАЗ.
public partial class UaResolveTaxInvoiceCommand
{
    public override async Task ExecuteAsync(TaxDocument document, CommandContext context)
    {
        var resolved = await context.GetService<IUaEInvoice>().ResolveInDoubtAsync(document.MetaId);

        context.AddClientAction(ClientAction.Message(resolved
            ? "Оператор відповів, стан накладної оновлено."
            : "Оператор ще не має відповіді — накладна лишається поданою."));
    }
}
