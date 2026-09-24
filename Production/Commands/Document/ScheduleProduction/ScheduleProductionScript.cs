using ZuloOne.Services.Contracts;

// Сроки операций: день каждой строки по последовательности и остатку
// мощности участка. Подтип не меняет — это план, не запуск.
public partial class ScheduleProductionCommand
{
    public override async Task ExecuteAsync(ProductionOrder document, CommandContext context)
    {
        var n = await context.GetService<IWorkCenterLoadService>().ScheduleAsync(document.MetaId);
        context.AddClientAction(ClientAction.Message(
            n == 0
                ? "Сроки не расставлены: нет операций или заказ уже завершён."
                : $"Сроки расставлены по последовательности и мощности: {n}."));
    }
}
