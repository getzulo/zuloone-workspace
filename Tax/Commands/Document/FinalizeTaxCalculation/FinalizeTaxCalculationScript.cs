using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

// «Утвердить расчёт»: ставка строки — та, что ITaxService видит на TaxPointDate,
// сумма = CalculateTax(база, ставка). Не invent свою арифметику.
public partial class FinalizeTaxCalculationCommand
{
    public override async Task ExecuteAsync(TaxCalculation document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<TaxCalculation>(document.MetaId);
        if (full == null) return;

        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Налоговый расчёт без строк не финализируется."));
            return;
        }

        var taxPoint = full.TaxPointDate.Date;
        var tax = context.GetService<ITaxService>();
        foreach (var line in full.Lines)
        {
            var effective = await tax.ResolveRateAsync(line.TaxCode, taxPoint);
            if (effective is null)
            {
                context.AddClientAction(ClientAction.Message(
                    $"На {taxPoint:yyyy-MM-dd} у налогового кода строки нет действующей ставки."));
                return;
            }
            if (effective.Value != line.RateValue)
            {
                context.AddClientAction(ClientAction.Message(
                    $"Ставка строки {line.RateValue} не действовала на {taxPoint:yyyy-MM-dd}: действующая {effective.Value}."));
                return;
            }

            var expected = tax.CalculateTax(line.TaxBase, effective.Value);
            if (Math.Abs(expected - line.TaxAmount) > 0.01m)
            {
                context.AddClientAction(ClientAction.Message(
                    $"Сумма налога {line.TaxAmount} не сходится с базой×ставкой ({expected})."));
                return;
            }
        }

        full.Subtype = TaxCalculation.Subtypes.Finalized;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Налоговый расчёт утверждён."));
    }
}
