#nullable enable
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class PurchaseCreditNoteEventHandler : TypedDocumentEventHandler<PurchaseCreditNote>
{
    public override async Task<EventResult> OnBeforeSaveAsync(PurchaseCreditNote header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;

        if (header.OriginalOrder == Guid.Empty)
            return EventResult.Ok();

        var order = await context.GetService<IDocumentManager>()
            .GetDocumentAsync<PurchaseOrder>(header.OriginalOrder);
        if (order is null)
            return EventResult.Ok();

        if (header.Supplier == Guid.Empty)
            header.Supplier = order.Supplier;
        if (header.LegalEntity == Guid.Empty)
        {
            var le = await context.GetService<IStoreCellService>().GetLegalEntityAsync(order.Location);
            if (le.HasValue && le.Value != Guid.Empty)
                header.LegalEntity = le.Value;
        }
        if (header.TaxRateApplied == 0m)
        {
            var tax = context.GetService<ITaxService>();
            var code = await tax.ResolveDefaultTaxCodeAsync();
            if (code is Guid taxCode)
            {
                var rate = await tax.ResolveRateAsync(taxCode, TaxPointOf(order));
                if (rate is decimal r)
                    header.TaxRateApplied = r;
            }
        }

        if (header.Lines.Count == 0)
        {
            foreach (var line in order.Lines)
            {
                header.Lines.Add(new PurchaseCreditNoteLinesTablePartRow
                {
                    Item = line.Item,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                });
            }
        }

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforePostAsync(PurchaseCreditNote document, EventContext context)
    {
        var prior = await next(document, context);
        if (!prior.Success) return prior;
        if (document.Subtype != PurchaseCreditNote.Subtypes.Posted)
            return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<PurchaseCreditNote>(document.MetaId) ?? document;
        if (full.OriginalOrder == Guid.Empty)
            return EventResult.Cancel("Укажите исходный заказ");

        var order = await docs.GetDocumentAsync<PurchaseOrder>(full.OriginalOrder);
        if (order is null || order.Subtype != PurchaseOrder.Subtypes.Received)
            return EventResult.Cancel("Кредит-нота только по оприходованному заказу");

        if (full.Supplier == Guid.Empty)
            return EventResult.Cancel("Укажите поставщика");
        if (full.Supplier != order.Supplier)
            return EventResult.Cancel("Поставщик ноты должен совпадать с заказом");
        if (full.LegalEntity == Guid.Empty)
        {
            var le = await context.GetService<IStoreCellService>().GetLegalEntityAsync(order.Location);
            if (le.HasValue && le.Value != Guid.Empty)
            {
                full.LegalEntity = le.Value;
                document.LegalEntity = le.Value;
            }
        }
        if (full.LegalEntity == Guid.Empty)
            return EventResult.Cancel("Не определено юрлицо покупателя — кредит-нота не проводится");

        var lines = full.Lines;
        if (lines.Count == 0)
            return EventResult.Cancel("Заполните строки кредит-ноты");
        if (lines.Any(l => l.Quantity <= 0m))
            return EventResult.Cancel("Количество в строке должно быть больше нуля");

        var rate = full.TaxRateApplied != 0m ? full.TaxRateApplied : 0m;
        if (rate <= 0m)
            return EventResult.Cancel("На приходе нет НДС — кредит-ноту выставлять нечего");

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnAfterPostAsync(PurchaseCreditNote header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        if (header.Subtype != PurchaseCreditNote.Subtypes.Posted)
            return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var note = await docs.GetDocumentAsync<PurchaseCreditNote>(header.MetaId);
        if (note is null || note.Lines.Count == 0) return EventResult.Ok();

        var legalEntity = note.LegalEntity;
        if (legalEntity == Guid.Empty) return EventResult.Ok();

        var pricing = context.GetService<IPricingService>();
        var taxBase = note.Lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice));
        var taxPoint = note.DocumentDate == default ? DateTime.UtcNow.Date : note.DocumentDate.Date;

        // Поставщик и юрлицо — по ним ключуются правила и ВСЕ сопоставления.
        // Без них возврат поставщику сторнировал по DefaultTaxCode, то есть мог
        // разойтись в ставке с приходом, который он же и возвращает.
        var tax = context.GetService<ITaxService>();
        var lineItems = note.Lines.Select(l => l.Item).ToList();
        var ctx = tax.DeterminationContext(
            legalEntity, Guid.Empty, note.Supplier,
            lineItems.Distinct().Count() == 1 ? lineItems[0] : Guid.Empty,
            await context.GetService<IItemClassification>().CommonGroupAsync(lineItems));
        ctx["document.type"] = "PurchaseCreditNote";
        ctx["direction"] = "INPUT";
        ctx["amount"] = taxBase;

        var calc = await tax
            .CreateReversalAsync(legalEntity, "INPUT", taxBase,
                $"Purchase credit note {header.MetaId:D}", taxPoint, ctx);
        if (calc.HasValue)
            await docs.AddLinkAsync(header.MetaId, calc.Value);

        return EventResult.Ok();
    }

    private static DateTime TaxPointOf(PurchaseOrder document)
        => document.DocumentDate == default ? DateTime.UtcNow.Date : document.DocumentDate.Date;
}
