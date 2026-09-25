#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Process B: Submitted → Confirmed (OnAfterPost: create invoice in Reserved + pick task).
// Delivered is set by SalesInvoiceEventHandler when invoice reaches Issued.
// OnBeforePost stock checks run only on Confirmed.
public partial class SalesOrderEventHandler : TypedDocumentEventHandler<SalesOrder>
{
    // Copy PaymentTerm and primary Contact from Customer when those fields are still empty.
    public override async Task<EventResult> OnBeforeSaveAsync(SalesOrder header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;
        if (!isNew)
            await MergeStoredHeaderAsync(header, context);
        var onDate = header.DeliveryDate != default ? header.DeliveryDate : DateTime.UtcNow;
        var contracts = context.GetService<ISalesContractService>();
        var stamp = await contracts.ResolveStampAsync(
            header.Customer, header.Outlet, header.Contract, onDate);
        ApplyStamp(header, stamp);

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
                if (header.PaymentTerm == Guid.Empty && customer.PaymentTerm != Guid.Empty)
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

    public override async Task<EventResult> OnAfterSaveAsync(SalesOrder header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;
        await LinkVisitAsync(header, context);
        return EventResult.Ok();
    }

    /// <summary>
    /// The order names the visit in <c>Visit</c>. The visit card reads document
    /// links, so the parent is the visit and the child is this order. Only
    /// AgentVisit parents are moved: a quotation parent stays.
    /// </summary>
    private static async Task LinkVisitAsync(SalesOrder header, EventContext context)
    {
        if (header.MetaId == Guid.Empty) return;
        var docs = context.GetService<IDocumentManager>();
        var stored = await docs.GetDocumentAsync<SalesOrder>(header.MetaId);
        var visitId = stored?.Visit ?? Guid.Empty;

        foreach (var parent in await docs.GetDocumentParentsAsync(header.MetaId))
        {
            if (parent == visitId) continue;
            if (await docs.GetDocumentAsync<AgentVisit>(parent) is null) continue;
            await docs.RemoveLinkAsync(parent, header.MetaId);
        }

        if (visitId == Guid.Empty) return;
        if (await docs.GetDocumentAsync<AgentVisit>(visitId) is null) return;
        await docs.AddLinkAsync(visitId, header.MetaId);
    }

    private static bool IsDraft(string? subtype)
        => string.IsNullOrEmpty(subtype)
           || string.Equals(subtype, "Draft", StringComparison.Ordinal);

    private static async Task PriceDraftLinesAsync(SalesOrder header, EventContext context)
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

    // SetSubtype / a dirty-field save send a partial header. Empty Guid here
    // means "not in the payload", not "clear the contract".
    private static async Task MergeStoredHeaderAsync(SalesOrder header, EventContext context)
    {
        if (header.MetaId == Guid.Empty) return;
        var stored = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesOrder>(header.MetaId);
        if (stored is null) return;
        if (header.Customer == Guid.Empty) header.Customer = stored.Customer;
        if (header.Outlet == Guid.Empty) header.Outlet = stored.Outlet;
        if (header.Contract == Guid.Empty) header.Contract = stored.Contract;
        if (header.Location == Guid.Empty) header.Location = stored.Location;
        if (header.DeliveryDate == default) header.DeliveryDate = stored.DeliveryDate;
        // Same partial-save rule as Contract: an empty Visit is "not in the
        // payload", not "detach the visit". A real move sends the new id.
        if (header.Visit == Guid.Empty) header.Visit = stored.Visit;
    }

    private static void ApplyStamp(SalesOrder header, Dictionary<string, object?> stamp)
    {
        if (header.Customer == Guid.Empty && stamp.TryGetValue("Customer", out var c) && c is Guid customer)
            header.Customer = customer;
        if (header.Outlet == Guid.Empty && stamp.TryGetValue("Outlet", out var o) && o is Guid outlet)
            header.Outlet = outlet;
        if (header.Contract == Guid.Empty && stamp.TryGetValue("Contract", out var k) && k is Guid contract)
            header.Contract = contract;
        if (header.PaymentTerm == Guid.Empty && stamp.TryGetValue("PaymentTerm", out var p) && p is Guid term)
            header.PaymentTerm = term;
        if (header.DeliveryTerm == Guid.Empty && stamp.TryGetValue("DeliveryTerm", out var d) && d is Guid delivery)
            header.DeliveryTerm = delivery;
        if (header.Contact == Guid.Empty && stamp.TryGetValue("Contact", out var n) && n is Guid contact)
            header.Contact = contact;
    }

    public override async Task<EventResult> OnBeforePostAsync(SalesOrder document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        // Stock sufficiency checks only apply when the order transitions to Confirmed (Approved).
        if (document.Subtype != "Confirmed")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesOrder>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;
        if (lines.Count == 0)
            return EventResult.Cancel("Заполните строки заказа");
        if (lines.Any(l => l.Quantity <= 0m))
            return EventResult.Cancel("Количество в строке должно быть больше нуля");

        var location = full != null ? full.Location : document.Location;
        var onDate = (full ?? document).DeliveryDate != default
            ? (full ?? document).DeliveryDate
            : DateTime.UtcNow;
        var contracts = context.GetService<ISalesContractService>();
        var pair = await contracts.ValidatePairAsync(
            (full ?? document).Customer, (full ?? document).Outlet, (full ?? document).Contract, onDate);
        if (pair != null)
            return EventResult.Cancel(pair);

        var pricing = context.GetService<IPricingService>();
        var discount = full?.DiscountPercent ?? document.DiscountPercent;
        var amount = lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice, discount));
        var settlement = await contracts.CheckSettlementAsync(
            (full ?? document).Customer, (full ?? document).Contract, amount);
        if (settlement != null)
            return EventResult.Cancel(settlement);

        var cells = context.GetService<IStoreCellService>();
        if (await cells.IsWarehouseDisciplineOnAsync() &&
            !await cells.IsCellAllowedForAsync(location, StoreCellPurpose.Picking))
            return EventResult.Cancel(
                "Заказ при адресной дисциплине отгружается из ячейки ОТБОРА — у выбранной ячейки другое назначение");

        var settings = (await context.GetService<IDictionaryManager<SalesSettings>>().GetRecordsAsync("1 = 1"))
            .FirstOrDefault();
        if (settings?.AllowBackorder == true)
            return EventResult.Ok();

        var fulfill = context.GetService<ISalesFulfillmentService>();
        foreach (var line in lines)
        {
            var free = await fulfill.AvailableQtyAsync(location, line.Item);
            if (free < line.Quantity)
                return EventResult.Cancel(
                    $"Не хватает свободного остатка: нужно {line.Quantity}, свободно {free}");
        }

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnAfterPostAsync(SalesOrder document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        // Invoice and the storage gather both run from ConfirmOrderAsync after
        // this save returns. A nested posting started here is swallowed.
        return EventResult.Ok();
    }
}
