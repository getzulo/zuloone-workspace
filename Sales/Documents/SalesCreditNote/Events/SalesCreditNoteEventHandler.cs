#nullable enable
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class SalesCreditNoteEventHandler : TypedDocumentEventHandler<SalesCreditNote>
{
    public override async Task<EventResult> OnBeforeSaveAsync(SalesCreditNote header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;

        if (header.OriginalInvoice == Guid.Empty)
            return EventResult.Ok();

        var invoice = await context.GetService<IDocumentManager>()
            .GetDocumentAsync<SalesInvoice>(header.OriginalInvoice);
        if (invoice is null)
            return EventResult.Ok();

        if (header.Customer == Guid.Empty)
            header.Customer = invoice.Customer;
        if (header.Outlet == Guid.Empty)
            header.Outlet = invoice.Outlet;
        if (header.Contract == Guid.Empty)
            header.Contract = invoice.Contract;
        if (header.LegalEntity == Guid.Empty)
            header.LegalEntity = invoice.LegalEntity;
        if (header.TaxRateApplied == 0m)
            header.TaxRateApplied = invoice.TaxRateApplied;

        if (header.Lines.Count == 0)
        {
            foreach (var line in invoice.Lines)
            {
                header.Lines.Add(new SalesCreditNoteLinesTablePartRow
                {
                    Item = line.Item,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                });
            }
        }

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforePostAsync(SalesCreditNote document, EventContext context)
    {
        var prior = await next(document, context);
        if (!prior.Success) return prior;
        if (document.Subtype != SalesCreditNote.Subtypes.Posted)
            return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesCreditNote>(document.MetaId) ?? document;
        if (full.OriginalInvoice == Guid.Empty)
            return EventResult.Cancel("Укажите исходный счёт");

        var invoice = await docs.GetDocumentAsync<SalesInvoice>(full.OriginalInvoice);
        if (invoice is null || invoice.Subtype != SalesInvoice.Subtypes.Issued)
            return EventResult.Cancel("Кредит-нота только по реализованному счёту");

        var pair = await context.GetService<ISalesContractService>()
            .ValidatePairAsync(full.Customer, full.Outlet, full.Contract, full.DocumentDate);
        if (pair != null)
            return EventResult.Cancel(pair);

        var lines = full.Lines;
        if (lines.Count == 0)
            return EventResult.Cancel("Заполните строки кредит-ноты");
        if (lines.Any(l => l.Quantity <= 0m))
            return EventResult.Cancel("Количество в строке должно быть больше нуля");

        var rate = full.TaxRateApplied != 0m ? full.TaxRateApplied : invoice.TaxRateApplied;
        if (rate <= 0m)
            return EventResult.Cancel("На счёте нет НДС — кредит-ноту выставлять нечего");

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnAfterPostAsync(SalesCreditNote header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        if (header.Subtype != SalesCreditNote.Subtypes.Posted)
            return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var note = await docs.GetDocumentAsync<SalesCreditNote>(header.MetaId);
        if (note is null || note.Lines.Count == 0) return EventResult.Ok();

        var legalEntity = note.LegalEntity;
        if (legalEntity == Guid.Empty) return EventResult.Ok();

        var pricing = context.GetService<IPricingService>();
        var taxBase = note.Lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice));
        var taxPoint = note.DocumentDate == default ? DateTime.UtcNow.Date : note.DocumentDate.Date;
        var calc = await context.GetService<ITaxService>()
            .CreateReversalAsync(legalEntity, "OUTPUT", taxBase,
                $"Sales credit note {header.MetaId:D}", taxPoint);
        if (calc.HasValue)
            await docs.AddLinkAsync(header.MetaId, calc.Value);

        return EventResult.Ok();
    }
}
