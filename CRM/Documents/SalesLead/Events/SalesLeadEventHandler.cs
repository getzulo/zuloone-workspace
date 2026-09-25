#nullable enable
using System;
using System.Collections.Generic;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class SalesLeadEventHandler : TypedDocumentEventHandler<SalesLead>
{
    public override async Task<EventResult> OnBeforeCreateAsync(SalesLead header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("SalesLead");
        if (header.DeliveryDate.Year < 1902)
        {
            var createDay = createDefaults.PickDay(createSeed, "DeliveryDate");
            if (createDay.Year >= 1902) header.DeliveryDate = createDay;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforeSaveAsync(SalesLead header, bool isNew, EventContext context)
    {
        if (!isNew)
            await MergeStoredHeaderAsync(header, context);
        if (header.DeliveryDate.Year < 1902)
            header.DeliveryDate = new DateTime(1900, 1, 1);

        if (string.IsNullOrWhiteSpace(header.Subject))
            return EventResult.Cancel("Укажите тему лида.");

        var onDate = header.DeliveryDate.Year >= 1902 ? header.DeliveryDate : DateTime.UtcNow;
        var contracts = context.GetService<ISalesContractService>();
        var stamp = await contracts.ResolveStampAsync(
            header.Customer, header.Outlet, header.Contract, onDate);
        ApplyStamp(header, stamp);

        if (header.Customer != Guid.Empty && header.Contract != Guid.Empty)
        {
            var pair = await contracts.ValidatePairAsync(
                header.Customer, header.Outlet, header.Contract, onDate);
            if (pair != null)
                return EventResult.Cancel(pair);
        }

        return EventResult.Ok();
    }

    private static async Task MergeStoredHeaderAsync(SalesLead header, EventContext context)
    {
        if (header.MetaId == Guid.Empty) return;
        var stored = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesLead>(header.MetaId);
        if (stored is null) return;
        if (header.Customer == Guid.Empty) header.Customer = stored.Customer;
        if (header.Outlet == Guid.Empty) header.Outlet = stored.Outlet;
        if (header.Contract == Guid.Empty) header.Contract = stored.Contract;
        if (header.Location == Guid.Empty) header.Location = stored.Location;
        if (header.DeliveryDate.Year < 1902 && stored.DeliveryDate.Year >= 1902)
            header.DeliveryDate = stored.DeliveryDate;
        if (string.IsNullOrWhiteSpace(header.Subject)) header.Subject = stored.Subject;
    }

    private static void ApplyStamp(SalesLead header, Dictionary<string, object?> stamp)
    {
        if (header.Customer == Guid.Empty && stamp.TryGetValue("Customer", out var c) && c is Guid customer)
            header.Customer = customer;
        if (header.Outlet == Guid.Empty && stamp.TryGetValue("Outlet", out var o) && o is Guid outlet)
            header.Outlet = outlet;
        if (header.Contract == Guid.Empty && stamp.TryGetValue("Contract", out var k) && k is Guid contract)
            header.Contract = contract;
        if (header.Contact == Guid.Empty && stamp.TryGetValue("Contact", out var n) && n is Guid contact)
            header.Contact = contact;
    }
}
