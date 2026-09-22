using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// Кнопка «Вивантажити єдиний розрахунок» на декларации.
//
// Это НЕ бланк ДПС J050010x. Додатків, ознак і XML схеми немає — файл
// честно называется рабочей таблицей ЄСВ+ПДФО+ВЗ. Команда, а не задание:
// выгрузка нужна по той декларации, которую бухгалтер смотрит. На обоих
// подтипах, как выгрузка ПДВ и UA-LEVY.
public partial class UaExportQuarterlyReturnCommand
{
    public override async Task ExecuteAsync(TaxReturn document, CommandContext context)
    {
        var id = await context.GetService<IUaTaxFiling>().ExportQuarterlyAsync(document.MetaId);
        if (id is null)
        {
            context.AddClientAction(ClientAction.Message("Декларацію не знайдено."));
            return;
        }

        context.AddClientAction(ClientAction.Message(
            "Робочу таблицю єдиного розрахунку сформовано: Довідники → Вивантаження декларації (тип UA-QPR)."));
    }
}
