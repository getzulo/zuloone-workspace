using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// Кнопка «Вивантажити бланки ДПС (J0500111)» на декларации.
//
// Три файла по официальным идентификаторам реестра ДПС (с 01.08.2026):
// J0500111 расчёт, J0510411 додаток 4ДФ, J0510111 додаток Д1.
// Это не XML кабинета: нет C_REG/C_STI. Нараховано — из регистров;
// виплачено/перераховано не подставляются.
public partial class UaExportDpsPayrollCommand
{
    public override async Task ExecuteAsync(TaxReturn document, CommandContext context)
    {
        var id = await context.GetService<IUaTaxFiling>().ExportDpsPayrollAsync(document.MetaId);
        if (id is null)
        {
            context.AddClientAction(ClientAction.Message("Декларацію не знайдено."));
            return;
        }

        context.AddClientAction(ClientAction.Message(
            "Бланки ДПС сформовано: Довідники → Вивантаження декларації (J0500111, J0510411, J0510111). Графи виплачено порожні."));
    }
}
