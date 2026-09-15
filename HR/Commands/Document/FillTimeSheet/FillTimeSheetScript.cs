using System;
using System.Threading.Tasks;
using ZuloOne.Services.Contracts;

public partial class FillTimeSheetCommand
{
    public override async Task ExecuteAsync(TimeSheet document, CommandContext context)
    {
        try
        {
            var filled = await context.GetService<ITimeSheetService>().FillStandardAsync(document.MetaId);
            context.AddClientAction(ClientAction.Message(filled == 0
                ? "Нечего заполнять: нет сотрудников подразделения или все дни уже стоят вручную."
                : $"Заполнено дней: {filled}."));
        }
        catch (InvalidOperationException ex)
        {
            context.AddClientAction(ClientAction.Message(ex.Message));
        }
    }
}
