using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// Кнопка «Вивантажити бланки ДПС (J0500111)» на декларации.
//
// Три файла по официальным идентификаторам реестра ДПС (с 01.08.2026):
// J0500111 расчёт, J0510411 додаток 4ДФ, J0510111 додаток Д1.
// CSV — перенос цифр; DeclarXml — unsigned DECLAR (наказ № 729) для M.E.Doc.
// Не контейнер № 499 і не КЕП кабінету.
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
            "Бланки ДПС сформовано: Довідники → Вивантаження декларації (CSV і XML DECLAR: J0500111, 4ДФ, Д1, Д5/Д6 за наявності подій). XML — імпорт у M.E.Doc, не КЕП кабінету. Д2 і Д3 звичайний роботодавець не подає."));
    }
}
