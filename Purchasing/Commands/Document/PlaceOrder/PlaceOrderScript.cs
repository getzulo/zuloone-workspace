using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// Команда «Заказать» на подтипе Draft: Черновик → Заказано. Если
// PurchasingSettings.AutoReceiveOnOrder — тот же клик принимает товар
// (Ordered, затем Received). Прыжок Draft → Received таблица переходов режет.
//
// Приёмка проверяется ДО ухода из Draft. Иначе непройденная проверка (ячейка не
// ПРИЁМКИ, нет действующей ставки на дату) оставляла бы заказ в Ordered —
// состоянии, которого никто не просил: пользователь нажал одну кнопку и получил
// половину её смысла. Сами проверки живут в IPurchaseReceiptService, и ReceiveOrder
// ходит той же дверью — правило на оба пути одно.
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

        var receipts = context.GetService<IPurchaseReceiptService>();

        // Ещё в Draft: отказ приёмки оставляет документ черновиком, а не Ordered.
        if (auto)
        {
            var blocked = await receipts.ValidateReceiveAsync(full.MetaId);
            if (blocked != null)
            {
                context.AddClientAction(ClientAction.Message(blocked));
                return;
            }
        }

        full.Subtype = PurchaseOrder.Subtypes.Ordered;
        await docs.SaveDocumentAsync(full);

        if (!auto)
        {
            context.AddClientAction(ClientAction.Message("Заказ размещён у поставщика."));
            return;
        }

        // Проверка повторяется внутри ReceiveAsync: между ней и переходом документ
        // сохранялся, и приход обязан судить по тому состоянию, в котором проводится.
        var error = await receipts.ReceiveAsync(full.MetaId);
        if (error != null)
        {
            context.AddClientAction(ClientAction.Message(error));
            return;
        }

        context.AddClientAction(ClientAction.Message("Заказ размещён и товар принят."));
    }
}
