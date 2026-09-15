using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// Команда «Согласовать заказ» (ApproveSalesOrder): переход Submitted → Confirmed.
// Проверяет строки, ячейку и свободный остаток.
// InvoiceOrderAsync вызывается здесь (не в OnAfterPost): внутри транзакции
// проведения сервисный IDocumentManager не видит незакоммиченные строки заказа.
public partial class ApproveSalesOrderCommand
{
    public override async Task ExecuteAsync(SalesOrder document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesOrder>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя согласовать пустой заказ: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Quantity <= 0m))
        {
            context.AddClientAction(ClientAction.Message("В каждой строке количество должно быть больше нуля."));
            return;
        }

        var cells = context.GetService<IStoreCellService>();
        if (await cells.IsWarehouseDisciplineOnAsync() &&
            !await cells.IsCellAllowedForAsync(full.Location, StoreCellPurpose.Picking))
        {
            context.AddClientAction(ClientAction.Message(
                "Заказ при адресной дисциплине отгружается из ячейки ОТБОРА — у выбранной ячейки другое назначение"));
            return;
        }

        var fulfill = context.GetService<ISalesFulfillmentService>();
        var settings = (await context.GetService<IDictionaryManager<SalesSettings>>().GetRecordsAsync("1 = 1"))
            .FirstOrDefault();
        if (settings?.AllowBackorder != true)
        {
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

        await fulfill.InvoiceOrderAsync(document.MetaId);

        context.AddClientAction(ClientAction.Message("Заказ согласован."));
    }
}
