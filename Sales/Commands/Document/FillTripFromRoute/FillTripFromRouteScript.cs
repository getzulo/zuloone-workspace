using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class FillTripFromRouteCommand
{
    public override async Task ExecuteAsync(DeliveryTrip document, CommandContext context)
    {
        var delivery = context.GetService<IDeliveryService>();
        var added = await delivery.FillTripFromRouteAsync(document.MetaId);
        context.AddClientAction(ClientAction.Message(added == 0
            ? "Подходящих заказов на маршруте нет."
            : $"Набрано точек: {added}."));
    }
}
