using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// Команда «Подтвердить заказ»: пустые строки и нехватка свободного остатка
// (Stock − Reserved) — отказ без смены подтипа. OnBeforePost дублирует те же
// правила для программного перехода; здесь сообщение уходит в UI, а не исключением.
public partial class ConfirmSalesOrderCommand
{
    public override async Task ExecuteAsync(SalesOrder document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesOrder>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя подтвердить пустой заказ: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Quantity <= 0m))
        {
            context.AddClientAction(ClientAction.Message("В каждой строке количество должно быть больше нуля."));
            return;
        }

        var settings = (await context.GetService<IDictionaryManager<SalesSettings>>().GetRecordsAsync("1 = 1"))
            .FirstOrDefault();
        if (settings?.AllowBackorder != true)
        {
            var fulfill = context.GetService<ISalesFulfillmentService>();
            foreach (var line in full.Lines)
            {
                var free = await fulfill.AvailableQtyAsync(full.Location, line.Item);
                if (free < line.Quantity)
                {
                    context.AddClientAction(ClientAction.Message(
                        $"Не хватает свободного остатка: нужно {line.Quantity}, свободно {free}"));
                    return;
                }
            }
        }

        full.Subtype = SalesOrder.Subtypes.Confirmed;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Заказ подтверждён."));
    }
}
