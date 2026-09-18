using System;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class ConvertQuotationCommand
{
    public override async Task ExecuteAsync(SalesQuotation document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesQuotation>(document.MetaId);
        if (full == null) return;

        if (full.ValidUntil.Year >= 1902 && full.ValidUntil.Date < DateTime.UtcNow.Date)
        {
            context.AddClientAction(ClientAction.Message("Срок действия КП истёк — заказ не создаётся."));
            return;
        }

        var orderId = await context.GetService<IQuotationService>().ConvertToOrderAsync(full.MetaId);
        if (orderId == Guid.Empty)
        {
            context.AddClientAction(ClientAction.Message("Не удалось создать заказ из КП."));
            return;
        }

        context.AddClientAction(ClientAction.Message("Создан заказ покупателя."));
    }
}
