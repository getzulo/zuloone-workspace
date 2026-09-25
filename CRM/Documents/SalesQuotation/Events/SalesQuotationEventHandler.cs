#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class SalesQuotationEventHandler : TypedDocumentEventHandler<SalesQuotation>
{
    public override async Task<EventResult> OnBeforeCreateAsync(SalesQuotation header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("SalesQuotation");
        if (header.DeliveryDate.Year < 1902)
        {
            var createDay = createDefaults.PickDay(createSeed, "DeliveryDate");
            if (createDay.Year >= 1902) header.DeliveryDate = createDay;
        }
        if (header.PaymentTerm == Guid.Empty)
        {
            var createId = createDefaults.Pick(createSeed, "PaymentTerm");
            if (createId != Guid.Empty) header.PaymentTerm = createId;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforeSaveAsync(SalesQuotation header, bool isNew, EventContext context)
    {
        if (!isNew)
            await MergeStoredHeaderAsync(header, context);
        // Optional DateTime: CLR default is year 1, SQL datetime starts at 1753.
        if (header.ValidUntil.Year < 1902)
            header.ValidUntil = new DateTime(1900, 1, 1);
        var onDate = header.DeliveryDate != default ? header.DeliveryDate : DateTime.UtcNow;
        var contracts = context.GetService<ISalesContractService>();
        var stamp = await contracts.ResolveStampAsync(
            header.Customer, header.Outlet, header.Contract, onDate);
        var createDefaults = context.GetService<IRecordDefaults>();
        var moduleTerm = createDefaults.Pick(await createDefaults.SeedAsync("SalesQuotation"), "PaymentTerm");
        ApplyStamp(header, stamp, moduleTerm);

        var pair = await contracts.ValidatePairAsync(
            header.Customer, header.Outlet, header.Contract, onDate);
        if (pair != null)
            return EventResult.Cancel(pair);

        if (header.Customer != Guid.Empty)
        {
            var dm = context.GetService<IDictionaryManager<Customer>>();
            var customer = await dm.GetRecordAsync(header.Customer);
            if (customer is not null)
            {
                if (createDefaults.IsPlaceholder(header.PaymentTerm, moduleTerm) && customer.PaymentTerm != Guid.Empty)
                    header.PaymentTerm = customer.PaymentTerm;

                if (header.Contact == Guid.Empty)
                {
                    var ccDm = context.GetService<IDictionaryManager<CustomerContact>>();
                    var contacts = await ccDm.GetRecordsAsync($"Customer = '{header.Customer}'");
                    var primary = contacts.FirstOrDefault(c => c.IsPrimary);
                    if (primary is not null)
                        header.Contact = primary.MetaId;
                }
            }
        }

        await PriceDraftLinesAsync(header, context);
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforePostAsync(SalesQuotation document, EventContext context)
    {
        if (document.Subtype != SalesQuotation.Subtypes.Issued)
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesQuotation>(document.MetaId);
        var header = full ?? document;
        var lines = full?.Lines ?? document.Lines;
        if (lines == null || lines.Count == 0)
            return EventResult.Cancel("Нельзя выставить пустое КП: добавьте строки.");
        if (lines.Any(l => l.Quantity <= 0m))
            return EventResult.Cancel("В каждой строке количество должно быть больше нуля.");
        if (header.Customer == Guid.Empty)
            return EventResult.Cancel("Укажите клиента.");
        if (header.Contract == Guid.Empty)
            return EventResult.Cancel("Укажите договор.");
        if (header.Location == Guid.Empty)
            return EventResult.Cancel("Укажите ячейку отгрузки.");
        if (header.DeliveryDate == default)
            return EventResult.Cancel("Укажите дату доставки.");

        var onDate = header.DeliveryDate;
        var pair = await context.GetService<ISalesContractService>().ValidatePairAsync(
            header.Customer, header.Outlet, header.Contract, onDate);
        if (pair != null)
            return EventResult.Cancel(pair);

        return EventResult.Ok();
    }

    private static bool IsDraft(string? subtype)
        => string.IsNullOrEmpty(subtype)
           || string.Equals(subtype, SalesQuotation.Subtypes.Draft, StringComparison.Ordinal);

    private static async Task PriceDraftLinesAsync(SalesQuotation header, EventContext context)
    {
        if (!IsDraft(header.Subtype) || header.Lines == null || header.Lines.Count == 0)
            return;
        var fill = context.GetService<ISalesLinePricing>();
        var onDate = header.DeliveryDate != default ? header.DeliveryDate : DateTime.UtcNow;
        foreach (var line in header.Lines)
        {
            var applied = await fill.ApplyFieldAsync(
                line.Unit == Guid.Empty ? "Item" : "Quantity",
                line.Item, line.Unit, line.Quantity, line.UnitPrice, line.PriceExplanation,
                header.Customer, header.Contract, onDate);
            if (applied.TryGetValue("Unit", out var u) && u is Guid unit) line.Unit = unit;
            if (applied.TryGetValue("Quantity", out var q) && q != null) line.Quantity = Convert.ToDecimal(q);
            if (applied.TryGetValue("UnitPrice", out var p) && p != null) line.UnitPrice = Convert.ToDecimal(p);
            if (applied.TryGetValue("PriceExplanation", out var e))
                line.PriceExplanation = e as string ?? "";
        }
    }

    private static async Task MergeStoredHeaderAsync(SalesQuotation header, EventContext context)
    {
        if (header.MetaId == Guid.Empty) return;
        var stored = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesQuotation>(header.MetaId);
        if (stored is null) return;
        if (header.Customer == Guid.Empty) header.Customer = stored.Customer;
        if (header.Outlet == Guid.Empty) header.Outlet = stored.Outlet;
        if (header.Contract == Guid.Empty) header.Contract = stored.Contract;
        if (header.Location == Guid.Empty) header.Location = stored.Location;
        if (header.DeliveryDate == default) header.DeliveryDate = stored.DeliveryDate;
        if (header.ValidUntil.Year < 1902 && stored.ValidUntil.Year >= 1902)
            header.ValidUntil = stored.ValidUntil;
    }

    private static void ApplyStamp(SalesQuotation header, Dictionary<string, object?> stamp, Guid moduleTerm)
    {
        if (header.Customer == Guid.Empty && stamp.TryGetValue("Customer", out var c) && c is Guid customer)
            header.Customer = customer;
        if (header.Outlet == Guid.Empty && stamp.TryGetValue("Outlet", out var o) && o is Guid outlet)
            header.Outlet = outlet;
        if (header.Contract == Guid.Empty && stamp.TryGetValue("Contract", out var k) && k is Guid contract)
            header.Contract = contract;
        if ((header.PaymentTerm == Guid.Empty || (moduleTerm != Guid.Empty && header.PaymentTerm == moduleTerm))
            && stamp.TryGetValue("PaymentTerm", out var p) && p is Guid term && term != Guid.Empty)
            header.PaymentTerm = term;
        if (header.DeliveryTerm == Guid.Empty && stamp.TryGetValue("DeliveryTerm", out var d) && d is Guid delivery)
            header.DeliveryTerm = delivery;
        if (header.Contact == Guid.Empty && stamp.TryGetValue("Contact", out var n) && n is Guid contact)
            header.Contact = contact;
    }
}
