using System;
using ZuloOne.Managers;

public partial class RejectSalesOrderCommand
{
    public override async Task ExecuteAsync(SalesOrder document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesOrder>(document.MetaId);
        if (full == null) return;

        if (string.IsNullOrWhiteSpace(full.RejectReason))
        {
            context.AddClientAction(ClientAction.Message("Укажите причину отклонения."));
            return;
        }

        full.Subtype = SalesOrder.Subtypes.Draft;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Заказ возвращён в черновик."));
    }
}
