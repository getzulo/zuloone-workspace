#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for SalesInvoice documents.
// `header` is a typed SalesInvoice entity — access fields directly (header.Number).
// Record events (insert/update/delete/validate) plus the document-only posting
// events are stubbed below. Cancel a transition with EventResult.Cancel("reason");
// document table-part rows are available via document.TableParts["Name"].
public partial class SalesInvoiceEventHandler : TypedDocumentEventHandler<SalesInvoice>
{
    // Building a new document server-side: seed header defaults (number, date).
    public override Task<EventResult> OnBeforeCreateAsync(SalesInvoice header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // MIQS BeforeSave: runs before ANY save — insert (isNew) or update.
    public override Task<EventResult> OnBeforeSaveAsync(SalesInvoice header, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(SalesInvoice header, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(SalesInvoice header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(SalesInvoice header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(SalesInvoice header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(SalesInvoice header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before the document is deleted.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the document was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before posting: reject overselling — a line cannot ship more than is on hand
    // at the sale location. Stock is a double-entry ledger (allowNegativeBalance:true),
    // so the engine no longer guards this; the check moves here (reads the location's
    // on-hand via IRegisterMovementService.GetBalanceAsync on the physical Item+Location
    // dimensions). Note: check-then-act, not atomic with posting.
    private static readonly Guid StockRegister = Guid.Parse("83559331-ac7f-46da-87a8-7da599ef6f41");

    public override async Task<EventResult> OnBeforePostAsync(SalesInvoice header, EventContext context)
    {
        if (header.Subtype != "Issued") return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesInvoice>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;

        var demand = new Dictionary<Guid, decimal>();
        foreach (var line in lines)
            demand[line.Item] = (demand.TryGetValue(line.Item, out var d) ? d : 0m) + line.Quantity;

        var stock = context.GetService<IRegisterMovementService>();
        foreach (var kv in demand)
        {
            var bal = await stock.GetBalanceAsync(StockRegister,
                new Dictionary<string, object?> { ["Item"] = kv.Key, ["Cell"] = header.Location });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            if (kv.Value > onHand)
                return EventResult.Cancel($"Недостаточно остатка на ячейке: требуется {kv.Value}, в наличии {onHand}");
        }
        return EventResult.Ok();
    }

    // Выставленный счёт порождает расчёт ВЫХОДНОГО налога: отдельный документ
    // TaxCalculation, связанный со счётом через граф документов. Отдельный
    // документ, а не поле на счёте, потому что налог живёт своей жизнью — у него
    // свой леджер, своя отчётность и своя дата налогового события.
    //
    // Порождение здесь, а не в проводке: ставка и код налога читаются из
    // справочников асинхронно, а GetTransactions синхронный.
    public override async Task<EventResult> OnAfterPostAsync(SalesInvoice header, EventContext context)
    {
        if (header.Subtype != "Issued") return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var invoice = await docs.GetDocumentAsync<SalesInvoice>(header.MetaId);
        if (invoice is null || invoice.Lines.Count == 0) return EventResult.Ok();

        // Юрлицо продавца — по ячейке отгрузки; база — та же сумма строк, от
        // которой считается выручка, чтобы налог и выручка не разъезжались.
        var legalEntity = await context.GetService<IStoreCellService>().GetLegalEntityAsync(invoice.Location);
        if (legalEntity is null) return EventResult.Ok();

        var pricing = context.GetService<IPricingService>();
        var taxBase = invoice.Lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice));

        // Контур необязателен: не настроен — сервис вернёт null, счёт выставлен как раньше.
        var calc = await context.GetService<ITaxService>()
            .CreateCalculationAsync(legalEntity.Value, "OUTPUT", taxBase, $"Sales invoice {header.Number}");
        if (calc.HasValue)
            await docs.AddLinkAsync(header.MetaId, calc.Value);

        return EventResult.Ok();
    }

    // Before unpost/cancel: about to reverse the document's movements.
    public override Task<EventResult> OnBeforeUnpostAsync(SalesInvoice header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the document's movements were reversed.
    public override Task<EventResult> OnAfterUnpostAsync(SalesInvoice header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Human-readable description shown in lists: put it in context.Data["description"].
    public override Task<EventResult> OnGenerateDescriptionAsync(SalesInvoice header, EventContext context)
    {
        // context.Data["description"] = "SalesInvoice " + header.Number;
        return Task.FromResult(EventResult.Ok());
    }

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(SalesInvoice header, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // A delete failed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());
}
