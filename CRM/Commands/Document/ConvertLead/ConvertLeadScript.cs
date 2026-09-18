using System;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class ConvertLeadCommand
{
    public override async Task ExecuteAsync(SalesLead document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesLead>(document.MetaId);
        if (full == null) return;

        if (full.Customer == Guid.Empty || full.Contract == Guid.Empty
            || full.Location == Guid.Empty || full.DeliveryDate.Year < 1902)
        {
            context.AddClientAction(ClientAction.Message(
                "Для КП нужны клиент, договор, ячейка отгрузки и дата доставки."));
            return;
        }

        var quoteId = await context.GetService<ILeadService>().CreateQuotationAsync(full.MetaId);
        if (quoteId == Guid.Empty)
        {
            context.AddClientAction(ClientAction.Message("Не удалось создать КП из лида."));
            return;
        }

        context.AddClientAction(ClientAction.Message("Создано коммерческое предложение."));
    }
}
