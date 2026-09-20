using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// «Отправить заказ»: Draft → Submitted, or ConfirmOrderAsync when
// SalesSettings.ConfirmOrderOnSubmit is on (same path as Approve).
public partial class SubmitSalesOrderCommand
{
    public override async Task ExecuteAsync(SalesOrder document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesOrder>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя отправить пустой заказ: добавьте строки."));
            return;
        }

        var settings = (await context.GetService<IDictionaryManager<SalesSettings>>().GetRecordsAsync("1 = 1"))
            .FirstOrDefault();
        if (settings?.ConfirmOrderOnSubmit == true)
        {
            var error = await context.GetService<ISalesFulfillmentService>().ConfirmOrderAsync(full.MetaId);
            if (error != null)
            {
                context.AddClientAction(ClientAction.Message(error));
                return;
            }
            context.AddClientAction(ClientAction.Message("Заказ согласован."));
            return;
        }

        full.Subtype = SalesOrder.Subtypes.Submitted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Заказ отправлен на согласование."));
    }
}
