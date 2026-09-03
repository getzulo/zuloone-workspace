using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// «Принять товар»: ячейка ПРИЁМКИ и действующая ставка налога на дату прихода.
// CreateCalculationAsync / задание раскладки — OnAfterPost, отсюда не зовём.
public partial class ReceiveOrderCommand
{
    public override async Task ExecuteAsync(PurchaseOrder document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<PurchaseOrder>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя принять пустой заказ: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Quantity <= 0m))
        {
            context.AddClientAction(ClientAction.Message("В каждой строке количество должно быть больше нуля."));
            return;
        }

        var cells = context.GetService<IStoreCellService>();
        if (!await cells.IsCellAllowedForAsync(full.Location, StoreCellPurpose.Receiving))
        {
            context.AddClientAction(ClientAction.Message(
                "Приход оформляется в ячейку ПРИЁМКИ — у выбранной ячейки другое назначение."));
            return;
        }

        var tax = context.GetService<ITaxService>();
        var taxPoint = full.DocumentDate == default ? DateTime.UtcNow.Date : full.DocumentDate.Date;
        var taxCode = await tax.ResolveDefaultTaxCodeAsync();
        if (taxCode is not null && await tax.ResolveRateAsync(taxCode.Value, taxPoint) is null)
        {
            context.AddClientAction(ClientAction.Message(
                $"Налоговый код настроен, но действующей ставки на {taxPoint:yyyy-MM-dd} нет — приход не проводится."));
            return;
        }

        full.Subtype = PurchaseOrder.Subtypes.Received;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Товар принят."));
    }
}
