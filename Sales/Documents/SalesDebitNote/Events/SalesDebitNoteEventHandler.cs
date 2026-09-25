using System;
#nullable enable
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class SalesDebitNoteEventHandler : TypedDocumentEventHandler<SalesDebitNote>
{
    public override async Task<EventResult> OnBeforeCreateAsync(SalesDebitNote header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("SalesDebitNote");
        if (header.LegalEntity == Guid.Empty)
        {
            var createId = createDefaults.Pick(createSeed, "LegalEntity");
            if (createId != Guid.Empty) header.LegalEntity = createId;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforeSaveAsync(SalesDebitNote header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;

        if (header.OriginalInvoice == Guid.Empty)
            return EventResult.Ok();

        var invoice = await context.GetService<IDocumentManager>()
            .GetDocumentAsync<SalesRealization>(header.OriginalInvoice);
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

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforePostAsync(SalesDebitNote document, EventContext context)
    {
        var prior = await next(document, context);
        if (!prior.Success) return prior;
        if (document.Subtype != SalesDebitNote.Subtypes.Posted)
            return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<SalesDebitNote>(document.MetaId) ?? document;
        if (full.OriginalInvoice == Guid.Empty)
            return EventResult.Cancel("Укажите исходный счёт");

        var invoice = await docs.GetDocumentAsync<SalesRealization>(full.OriginalInvoice);
        if (invoice is null || invoice.Subtype != SalesRealization.Subtypes.Issued)
            return EventResult.Cancel("Дебет-нота только по реализованному счёту");

        var pair = await context.GetService<ISalesContractService>()
            .ValidatePairAsync(full.Customer, full.Outlet, full.Contract, full.DocumentDate);
        if (pair != null)
            return EventResult.Cancel(pair);

        var lines = full.Lines;
        if (lines.Count == 0)
            return EventResult.Cancel("Заполните строки дебет-ноты");
        if (lines.Any(l => l.Quantity <= 0m))
            return EventResult.Cancel("Количество в строке должно быть больше нуля");

        var rate = full.TaxRateApplied != 0m ? full.TaxRateApplied : invoice.TaxRateApplied;
        if (rate <= 0m)
            return EventResult.Cancel("На счёте нет НДС — дебет-ноту выставлять нечего");

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnAfterPostAsync(SalesDebitNote header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        if (header.Subtype != SalesDebitNote.Subtypes.Posted)
            return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var note = await docs.GetDocumentAsync<SalesDebitNote>(header.MetaId);
        if (note is null || note.Lines.Count == 0) return EventResult.Ok();

        var legalEntity = note.LegalEntity;
        if (legalEntity == Guid.Empty) return EventResult.Ok();

        var pricing = context.GetService<IPricingService>();
        var taxBase = note.Lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice));
        var taxPoint = note.DocumentDate == default ? DateTime.UtcNow.Date : note.DocumentDate.Date;

        // Стороны сделки — как на счёте: по ним ключуются и правила, и ВСЕ
        // сопоставления. Без них дебет-нота определялась по DefaultTaxCode и
        // расходилась в ставке с тем счётом, который она же и дополняет.
        var tax = context.GetService<ITaxService>();
        var lineItems = note.Lines.Select(l => l.Item).ToList();
        var ctx = tax.DeterminationContext(
            legalEntity, note.Customer, Guid.Empty,
            lineItems.Distinct().Count() == 1 ? lineItems[0] : Guid.Empty,
            await context.GetService<IItemClassification>().CommonGroupAsync(lineItems));
        ctx["document.type"] = "SalesDebitNote";
        ctx["direction"] = "OUTPUT";
        ctx["amount"] = taxBase;

        var calc = await tax
            .CreateCalculationAsync(legalEntity, "OUTPUT", taxBase,
                $"Sales debit note {header.MetaId:D}", taxPoint, ctx);
        if (calc.HasValue)
            await docs.AddLinkAsync(header.MetaId, calc.Value);

        return EventResult.Ok();
    }
}
