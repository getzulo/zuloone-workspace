#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Вечерний заказ → утренний счёт. Счёт не создаётся руками из события: один
// сервис закрывает и одиночную доставку, и рейс. Повторный вызов находит уже
// выставленный счёт по SourceOrder и не плодит второй.
public partial class SalesFulfillmentService
{
    private static readonly Guid SalesInvoiceType = Guid.Parse("34a1af4c-aeaf-48d1-8626-9a0a13b2d5c3");
    private static readonly Guid SalesOrderType = Guid.Parse("23643b1b-b959-4206-83ab-948c713276c9");

    private readonly IDocumentManager _documents;
    private readonly IDocumentPostingService _posting;
    private readonly ITotalsManager _totals;
    private readonly IDataService _data;

    public SalesFulfillmentService(
        IDocumentManager documents,
        IDocumentPostingService posting,
        ITotalsManager totals,
        IDataService data)
    {
        _documents = documents;
        _posting = posting;
        _totals = totals;
        _data = data;
    }

    /// <summary>Свободно = Stock − ReservedStock по ячейке и товару.</summary>
    public async Task<decimal> AvailableQtyAsync(Guid cell, Guid item)
    {
        if (cell == Guid.Empty || item == Guid.Empty) return 0m;
        var dims = new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item };
        var stock = await _totals.GetBalanceAsync("Stock", "Qty", dims);
        var reserved = await _totals.GetBalanceAsync("ReservedStock", "Qty", dims);
        return stock - reserved;
    }

    /// <summary>Выставить счёт по заказу. Пустой Guid — строк на отгрузку нет
    /// или заказ не найден. Уже выставленный счёт возвращается как есть.</summary>
    public async Task<Guid> InvoiceOrderAsync(Guid orderId)
    {
        if (orderId == Guid.Empty) return Guid.Empty;

        var existing = await _documents.CountDocumentsAsync<SalesInvoice>(
            $"SourceOrder = '{orderId}'");
        if (existing > 0)
        {
            var found = (await _documents.QueryDocumentsAsync<SalesInvoice>(
                $"SourceOrder = '{orderId}'")).FirstOrDefault();
            return found?.MetaId ?? Guid.Empty;
        }

        var order = await _documents.GetDocumentAsync<SalesOrder>(orderId);
        if (order == null || order.Lines.Count == 0) return Guid.Empty;

        var invoice = await _documents.NewDocumentAsync<SalesInvoice>();
        invoice.Customer = order.Customer;
        invoice.Location = order.Location;
        invoice.SourceOrder = order.MetaId;
        if (order.DeliveryDate != default)
            invoice.DocumentDate = order.DeliveryDate.Date;

        foreach (var line in order.Lines)
        {
            var qty = line.QtyDelivered > 0m ? line.QtyDelivered : line.Quantity;
            if (qty <= 0m) continue;
            invoice.Lines.Add(new SalesInvoiceLinesTablePartRow
            {
                Item = line.Item,
                Quantity = qty,
                UnitPrice = line.UnitPrice
            });
        }

        if (invoice.Lines.Count == 0) return Guid.Empty;

        await _documents.SaveDocumentAsync(invoice);
        await _posting.SetSubtypeAsync(SalesInvoiceType, invoice.MetaId, "Issued");
        await _documents.AddLinkAsync(order.MetaId, invoice.MetaId);
        return invoice.MetaId;
    }

    /// <summary>Закрыть точки рейса: отказ → Cancelled, иначе Delivered
    /// (счёт ставит обработчик заказа). Повтор безопасен.</summary>
    public async Task CompleteTripAsync(Guid tripId)
    {
        var trip = await _documents.GetDocumentAsync<DeliveryTrip>(tripId);
        if (trip == null) return;

        foreach (var stop in trip.Lines.OrderBy(l => l.StopSequence))
        {
            if (stop.SalesOrder is not Guid orderId || orderId == Guid.Empty) continue;
            var order = await _documents.GetDocumentAsync<SalesOrder>(orderId);
            if (order == null) continue;
            if (order.Subtype == "Delivered" || order.Subtype == "Cancelled")
                continue;

            var outcome = (stop.Outcome ?? string.Empty).Trim();
            if (string.Equals(outcome, "Refused", StringComparison.OrdinalIgnoreCase))
            {
                if (order.Subtype == "Confirmed")
                    await _posting.SetSubtypeAsync(SalesOrderType, order.MetaId, "Cancelled");
                continue;
            }

            if (string.Equals(outcome, "Partial", StringComparison.OrdinalIgnoreCase)
                && stop.QtyShipped > 0m
                && order.Lines.Count == 1)
            {
                var line = order.Lines[0];
                await _data.UpdateAsync("TP_SalesOrderLines", line.MetaId,
                    new Dictionary<string, object?> { ["QtyDelivered"] = stop.QtyShipped });
            }

            if (order.Subtype == "Draft")
                await _posting.SetSubtypeAsync(SalesOrderType, order.MetaId, "Confirmed");
            await _posting.SetSubtypeAsync(SalesOrderType, order.MetaId, "Delivered");
            await _documents.AddLinkAsync(trip.MetaId, order.MetaId);
        }
    }
}
