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

        try
        {
            var taxId = await CreateOutputTaxAsync(header, context);
            if (taxId.HasValue)
                await context.GetService<IDocumentManager>().AddLinkAsync(header.MetaId, taxId.Value);
        }
        catch
        {
            // Налоговый контур настраивается отдельно: без кода налога по умолчанию
            // счёт обязан выставляться как раньше.
        }

        return EventResult.Ok();
    }

    /// <summary>Расчёт выходного налога по строкам счёта; null, если контур не настроен.</summary>
    private async Task<Guid?> CreateOutputTaxAsync(SalesInvoice header, EventContext context)
    {
        // Код налога по умолчанию — из настроек модуля Tax (строковый код, не ссылка).
        var settings = (await context.GetService<IDictionaryManager<TaxSettings>>()
            .GetRecordsAsync("1 = 1")).FirstOrDefault();
        if (settings == null || string.IsNullOrWhiteSpace(settings.DefaultTaxCode)) return null;

        var codes = context.GetService<IDictionaryManager<TaxCode>>();
        var taxCode = (await codes.GetRecordsAsync($"Code = '{settings.DefaultTaxCode}'")).FirstOrDefault();
        if (taxCode == null) return null;

        var rate = await context.GetService<ITaxService>().ResolveRateAsync(taxCode.MetaId);
        if (rate == null) return null;

        var docs = context.GetService<IDocumentManager>();
        var invoice = await docs.GetDocumentAsync<SalesInvoice>(header.MetaId);
        if (invoice == null || invoice.Lines.Count == 0) return null;

        // База — сумма строк счёта; та же арифметика, что у выручки, чтобы налог
        // и выручка считались от ОДНОГО числа.
        var taxBase = invoice.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
        if (taxBase <= 0m) return null;

        var direction = (await context.GetService<IDictionaryManager<TaxDirection>>()
            .GetRecordsAsync("Code = 'OUTPUT'")).FirstOrDefault();
        if (direction == null) return null;

        var customer = await context.GetService<IDictionaryManager<Customer>>().GetRecordAsync(invoice.Customer);
        var le = customer == null ? Guid.Empty : Guid.Empty;   // юрлицо продавца — из ячейки отгрузки
        var cell = await context.GetService<IDictionaryManager<StoreCell>>().GetRecordAsync(invoice.Location);
        if (cell == null) return null;
        var zone = await context.GetService<IDictionaryManager<StoreZone>>().GetRecordAsync(cell.StoreZone);
        if (zone == null) return null;
        var store = await context.GetService<IDictionaryManager<Store>>().GetRecordAsync(zone.Store);
        if (store == null) return null;
        var div = await context.GetService<IDictionaryManager<Division>>().GetRecordAsync(store.Division);
        if (div == null) return null;
        var legalEntity = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(div.LegalEntity);
        if (legalEntity == null) return null;

        var calc = await docs.NewDocumentAsync<TaxCalculation>("Draft", new Dictionary<string, object?>
        {
            ["LegalEntity"] = legalEntity.MetaId,
            ["Currency"] = legalEntity.Currency,
            ["TaxPointDate"] = DateTime.UtcNow.Date,
            ["DeterminationReason"] = "Sales invoice " + header.MetaId,
        });

        var taxAmount = context.GetService<ITaxService>().CalculateTax(taxBase, rate.Value);
        calc.Lines.Add(new TaxCalculationLinesTablePartRow
        {
            Direction = direction.MetaId,
            TaxCode = taxCode.MetaId,
            RateValue = rate.Value,
            TaxBase = taxBase,
            TaxAmount = taxAmount,
        });

        await docs.SaveDocumentAsync(calc);
        await context.GetService<IDocumentPostingService>()
            .SetSubtypeAsync(TaxCalculationType, calc.MetaId, "Finalized");
        return calc.MetaId;
    }

    private static readonly Guid TaxCalculationType = Guid.Parse("1e07e7a9-d80f-4067-bc65-e40c96d4feee");

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
