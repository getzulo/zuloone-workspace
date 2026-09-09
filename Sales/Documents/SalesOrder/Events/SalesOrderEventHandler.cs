#nullable enable
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

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforePostAsync(SalesOrder document, EventContext context)
    {
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

    public override async Task<EventResult> OnAfterPostAsync(SalesOrder document, EventContext context)
    {
        // Счёт создаёт ApproveSalesOrderCommand после SaveDocumentAsync:
        // InvoiceOrderAsync из этой транзакции не видит незакоммиченные строки.
        if (document.Subtype == "Confirmed")
            await context.GetService<ISalesFulfillmentService>().EnsurePickTaskAsync(document.MetaId);
        return EventResult.Ok();
    }
}
