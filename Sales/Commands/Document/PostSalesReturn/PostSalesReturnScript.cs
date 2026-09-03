using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// «Провести возврат»: ячейка ПРИЁМКИ и ставка налога на дату. Суммы строк —
// IPricingService.LineAmount (та же арифметика, что в проводках возврата).
public partial class PostSalesReturnCommand
{
    public override async Task ExecuteAsync(SalesReturn document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesReturn>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя провести пустой возврат: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Quantity <= 0m))
        {
            context.AddClientAction(ClientAction.Message("Количество в строке должно быть больше нуля."));
            return;
        }

        if (!await context.GetService<IStoreCellService>()
                .IsCellAllowedForAsync(full.Location, StoreCellPurpose.Receiving))
        {
            context.AddClientAction(ClientAction.Message(
                "Возврат принимается в ячейку ПРИЁМКИ — у выбранной ячейки другое назначение."));
            return;
        }

        var pricing = context.GetService<IPricingService>();
        if (full.Lines.Any(l => pricing.LineAmount(l.Quantity, l.UnitPrice) <= 0m))
        {
            context.AddClientAction(ClientAction.Message("Сумма строки должна быть больше нуля."));
            return;
        }

        var tax = context.GetService<ITaxService>();
        var taxCode = await tax.ResolveDefaultTaxCodeAsync();
        if (taxCode is not null)
        {
            var taxPoint = full.DocumentDate == default ? DateTime.UtcNow.Date : full.DocumentDate.Date;
            if (await tax.ResolveRateAsync(taxCode.Value, taxPoint) is null)
            {
                context.AddClientAction(ClientAction.Message(
                    $"Налоговый код настроен, но действующей ставки на {taxPoint:yyyy-MM-dd} нет — возврат не проводится."));
                return;
            }
        }

        full.Subtype = SalesReturn.Subtypes.Posted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Возврат проведён."));
    }
}
