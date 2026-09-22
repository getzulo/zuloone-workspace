using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// Кнопка «Вивантажити утримання (таблиця 4ДФ)» на декларации.
//
// Это НЕ бланк ДПС. Ознаки доходу и нараховано/виплачено в UaPayrollLevy нет —
// файл честно называется рабочей таблицей. Команда, а не задание: выгрузка
// нужна по той декларации, которую бухгалтер смотрит. На обоих подтипах, как
// выгрузка ПДВ: сданную сверяют чаще черновика.
public partial class UaExportPayrollLevyCommand
{
    public override async Task ExecuteAsync(TaxReturn document, CommandContext context)
    {
        var id = await context.GetService<IUaTaxFiling>().ExportLeviesAsync(document.MetaId);
        if (id is null)
        {
            context.AddClientAction(ClientAction.Message("Декларацію не знайдено."));
            return;
        }

        context.AddClientAction(ClientAction.Message(
            "Робочу таблицю утримань сформовано: Довідники → Вивантаження декларації (тип UA-LEVY)."));
    }
}
