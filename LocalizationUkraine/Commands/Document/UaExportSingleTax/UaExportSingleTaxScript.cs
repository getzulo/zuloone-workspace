using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// Кнопка «Вивантажити єдиний податок (J0103509)» на декларации.
//
// Отдельная команда, не VatReturnType: пустой тип в настройках остаётся
// декларацией ПДВ J0200126. Форма юрособи 3 групи — наказ Мінфіну 31.01.2025
// № 57. Не XML кабинета. UA-EP15 не кладётся в рядок 2 (там 6%/10%, не 15%).
public partial class UaExportSingleTaxCommand
{
    public override async Task ExecuteAsync(TaxReturn document, CommandContext context)
    {
        var id = await context.GetService<IUaTaxFiling>().ExportAsync(document.MetaId, "J0103509");
        if (id is null)
        {
            context.AddClientAction(ClientAction.Message("Декларацію не знайдено."));
            return;
        }

        context.AddClientAction(ClientAction.Message(
            "Вивантаження сформовано: Довідники → Вивантаження декларації (J0103509). Період — з 1 січня. Додаток МПЗ порожній."));
    }
}
