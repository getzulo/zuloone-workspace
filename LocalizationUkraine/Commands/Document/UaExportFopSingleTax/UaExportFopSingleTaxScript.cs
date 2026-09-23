using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// Кнопка «Вивантажити єдиний податок ФОП (F0103309)» на декларации.
//
// Отдельная команда от ЮО J0103509: у ФОП рядок 07 = 15 %, у юрособи двойная
// ставка 6/10 %. Не XML кабинета. Додатки ЄСВ за себе і МПЗ порожні.
public partial class UaExportFopSingleTaxCommand
{
    public override async Task ExecuteAsync(TaxReturn document, CommandContext context)
    {
        var id = await context.GetService<IUaTaxFiling>().ExportAsync(document.MetaId, "F0103309");
        if (id is null)
        {
            context.AddClientAction(ClientAction.Message("Декларацію не знайдено."));
            return;
        }

        context.AddClientAction(ClientAction.Message(
            "Вивантаження сформовано: Довідники → Вивантаження декларації (F0103309). Період — з 1 січня. Додатки F0133109 і F0133209 порожні."));
    }
}
