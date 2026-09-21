using System.Linq;
using ZuloOne.Managers;

// Команда «Заказать» на подтипе Draft: Черновик → Заказано. Если
// PurchasingSettings.AutoReceiveOnOrder — тот же клик принимает товар
// (Ordered, затем Received). Прыжок Draft → Received таблица переходов режет.
public partial class PlaceOrderCommand
{
    public override async Task ExecuteAsync(PurchaseOrder document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();

        var full = await docs.GetDocumentAsync<PurchaseOrder>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя заказать пустой документ: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Quantity <= 0m))
        {
            context.AddClientAction(ClientAction.Message("В каждой строке количество должно быть больше нуля."));
            return;
        }

        var settings = (await context.GetService<IDictionaryManager<PurchasingSettings>>().GetRecordsAsync("1 = 1"))
            .FirstOrDefault();
        var auto = settings?.AutoReceiveOnOrder == true;

        full.Subtype = PurchaseOrder.Subtypes.Ordered;
        await docs.SaveDocumentAsync(full);

        if (!auto)
        {
            context.AddClientAction(ClientAction.Message("Заказ размещён у поставщика."));
            return;
        }

        full.Subtype = PurchaseOrder.Subtypes.Received;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Заказ размещён и товар принят."));
    }
}
