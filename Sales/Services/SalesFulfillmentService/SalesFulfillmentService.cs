#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Evening order → morning invoice. The invoice is not created by hand from an
// event: one service covers both a single delivery and a trip. A repeat call
// finds the already-issued invoice by SourceOrder and does not spawn a second.
public partial class SalesFulfillmentService
{
    private static readonly Guid SalesInvoiceType = Guid.Parse("34a1af4c-aeaf-48d1-8626-9a0a13b2d5c3");
    private static readonly Guid SalesOrderType = Guid.Parse("23643b1b-b959-4206-83ab-948c713276c9");
    private static readonly Guid StockTransferType = Guid.Parse("242aad95-3647-45b4-bcf7-e3e969d233ba");
    private static readonly Guid PickTaskType = Guid.Parse("57100801-0000-4000-8000-000000000000");

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

    // A foreign model service — not in the constructor: the ISalesFulfillmentService
    // factory then fails to start ("service is not available"). Same as PricingService.
    private static IStoreCellService Cells => ScriptServices.Get<IStoreCellService>();

    /// <summary>Submitted → Confirmed + invoice. Shared by Approve and by
    /// Submit when <c>ConfirmOrderOnSubmit</c> is on. Null = success.</summary>
    public async Task<string?> ConfirmOrderAsync(Guid orderId)
    {
        var full = await _documents.GetDocumentAsync<SalesOrder>(orderId);
        if (full == null) return "Заказ не найден.";
        if (full.Lines.Count == 0)
            return "Нельзя согласовать пустой заказ: добавьте строки.";
        if (full.Lines.Any(l => l.Quantity <= 0m))
            return "В каждой строке количество должно быть больше нуля.";

        var cells = Cells;
        if (await cells.IsWarehouseDisciplineOnAsync() &&
            !await cells.IsCellAllowedForAsync(full.Location, StoreCellPurpose.Picking))
            return "Заказ при адресной дисциплине отгружается из ячейки ОТБОРА — у выбранной ячейки другое назначение";

        var onDate = full.DeliveryDate != default ? full.DeliveryDate : DateTime.UtcNow;
        var contracts = ScriptServices.Get<ISalesContractService>();
        var pair = await contracts.ValidatePairAsync(full.Customer, full.Outlet, full.Contract, onDate);
        if (pair != null) return pair;

        var pricing = ScriptServices.Get<IPricingService>();
        var amount = full.Lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice, full.DiscountPercent));
        var settlement = await contracts.CheckSettlementAsync(full.Customer, full.Contract, amount);
        if (settlement != null) return settlement;

        var settings = (await ScriptServices.Get<IDictionaryManager<SalesSettings>>().GetRecordsAsync("1 = 1"))
            .FirstOrDefault();
        if (settings?.AllowBackorder != true)
        {
            foreach (var line in full.Lines)
            {
                var free = await AvailableQtyAsync(full.Location, line.Item);
                if (free < line.Quantity)
                    return $"Не хватает свободного остатка: нужно {line.Quantity}, свободно {free}";
            }
        }

        full.Subtype = SalesOrder.Subtypes.Confirmed;
        await _documents.SaveDocumentAsync(full);
        // After the order posting returns. A transfer posted from OnAfterPost
        // is swallowed, the same way a nested invoice used to be.
        await PrepareShipmentAsync(orderId);
        await InvoiceOrderAsync(orderId);
        return null;
    }

    /// <summary>Available = Stock − ReservedStock. Without discipline — by the
    /// order cell. With discipline the goods are still in storage while the order
    /// points at picking: look at every cell of that cell's store, otherwise
    /// confirmation always reports "no stock".</summary>
    public async Task<decimal> AvailableQtyAsync(Guid cell, Guid item)
    {
        if (cell == Guid.Empty || item == Guid.Empty) return 0m;
        if (await Cells.IsWarehouseDisciplineOnAsync())
        {
            var store = await Cells.GetStoreAsync(cell);
            if (store is Guid storeId)
            {
                decimal stock = 0m, reserved = 0m;
                foreach (var c in await Cells.GetCellsOfStoreAsync(storeId))
                {
                    var dims = new Dictionary<string, object?> { ["Cell"] = c, ["Item"] = item };
                    stock += await _totals.GetBalanceAsync("Stock", "Qty", dims);
                    reserved += await _totals.GetBalanceAsync("ReservedStock", "Qty", dims);
                }
                return stock - reserved;
            }
        }

        var one = new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item };
        return await _totals.GetBalanceAsync("Stock", "Qty", one)
             - await _totals.GetBalanceAsync("ReservedStock", "Qty", one);
    }

    /// <summary>
    /// Simple scheme posts transfers onto the write-off cell. Extended and an
    /// empty scheme leave a draft pick for the warehouse to confirm. Discipline
    /// off — neither. A second call finds the child already linked and stops.
    /// </summary>
    public async Task<Guid> PrepareShipmentAsync(Guid orderId)
    {
        if (orderId == Guid.Empty) return Guid.Empty;
        if (!await Cells.IsWarehouseDisciplineOnAsync()) return Guid.Empty;
        if (await LinkedChildAsync(orderId, StockTransferType) is Guid transfer) return transfer;
        if (await LinkedChildAsync(orderId, PickTaskType) is Guid pick) return pick;
        if (await SchemeAsync() == WarehouseFulfillmentScheme.Simple)
            return await ConsolidateStorageAsync(orderId);
        return await EnsurePickDraftAsync(orderId);
    }

    /// <summary>
    /// Posted stock transfers that gather a confirmed order onto the cell the
    /// invoice will write off (<c>SalesOrder.Location</c>). Only the simple
    /// shipment scheme. Called after the order save has returned: posting a
    /// child from <c>OnAfterPost</c> is swallowed.
    ///
    /// Per line, storage cells of that store are summed first. Short of the
    /// line — no transfer for it (a partial gather cannot be issued). Enough —
    /// one posted <c>StockTransfer</c> per source cell, only for the quantity
    /// the line still needs, onto the write-off cell. The sales order is the
    /// parent of each transfer (document link). A repeat returns the first
    /// linked transfer and does not move stock again.
    /// </summary>
    public async Task<Guid> ConsolidateStorageAsync(Guid orderId)
    {
        if (orderId == Guid.Empty) return Guid.Empty;
        if (!await Cells.IsWarehouseDisciplineOnAsync()) return Guid.Empty;
        if (await SchemeAsync() != WarehouseFulfillmentScheme.Simple) return Guid.Empty;

        var family = await _documents.GetDocumentFamilyAsync(orderId);
        var transferIds = new HashSet<Guid>(
            family.Nodes.Where(n => n.DocTypeMetaId == StockTransferType).Select(n => n.DocId));
        var existing = family.Edges.FirstOrDefault(e => e.ParentDocId == orderId && transferIds.Contains(e.ChildDocId));
        if (existing != null) return existing.ChildDocId;

        var order = await _documents.GetDocumentAsync<SalesOrder>(orderId);
        if (order == null || order.Lines.Count == 0 || order.Location == Guid.Empty) return Guid.Empty;

        var store = await Cells.GetStoreAsync(order.Location);
        if (store is null) return Guid.Empty;
        var cells = await StorageCellsInPickOrderAsync(store.Value);
        cells.RemoveAll(c => c == order.Location);
        if (cells.Count == 0) return Guid.Empty;

        var onHand = new Dictionary<(Guid Cell, Guid Item), decimal>();
        var buckets = new Dictionary<Guid, Dictionary<Guid, decimal>>();
        foreach (var line in order.Lines)
        {
            if (line.Quantity <= 0m || line.Item == Guid.Empty) continue;
            decimal total = 0m;
            foreach (var cell in cells)
                total += await FreeAtAsync(onHand, cell, line.Item);
            if (total < line.Quantity) continue;

            var remaining = line.Quantity;
            foreach (var cell in cells)
            {
                if (remaining <= 0m) break;
                var free = await FreeAtAsync(onHand, cell, line.Item);
                if (free <= 0m) continue;
                var take = free < remaining ? free : remaining;
                AddPick(buckets, cell, line.Item, take);
                onHand[(cell, line.Item)] = free - take;
                remaining -= take;
            }
        }
        if (buckets.Count == 0) return Guid.Empty;

        Guid firstTransfer = Guid.Empty;
        foreach (var cell in cells)
        {
            if (!buckets.TryGetValue(cell, out var lines)) continue;
            var transfer = await _documents.NewDocumentAsync<StockTransfer>("Draft", new Dictionary<string, object?>
            {
                ["FromCell"] = cell,
                ["ToCell"] = order.Location,
            });
            foreach (var (item, qty) in lines)
            {
                transfer.Lines.Add(new StockTransferLinesTablePartRow
                {
                    Item = item,
                    Quantity = qty,
                });
            }
            await _documents.SaveDocumentAsync(transfer);
            await _posting.SetSubtypeAsync(StockTransferType, transfer.MetaId, "Posted");
            await _documents.AddLinkAsync(order.MetaId, transfer.MetaId);
            if (firstTransfer == Guid.Empty) firstTransfer = transfer.MetaId;
        }
        return firstTransfer;
    }

    /// <summary>One draft pick from the first storage cell, for the whole line.
    /// The warehouse confirms it. Stock stays where it is.</summary>
    private async Task<Guid> EnsurePickDraftAsync(Guid orderId)
    {
        var order = await _documents.GetDocumentAsync<SalesOrder>(orderId);
        if (order == null || order.Lines.Count == 0 || order.Location == Guid.Empty) return Guid.Empty;

        var store = await Cells.GetStoreAsync(order.Location);
        if (store is null) return Guid.Empty;
        var storage = await Cells.SuggestStorageCellAsync(store.Value);
        if (storage is null) return Guid.Empty;

        var task = await _documents.NewDocumentAsync<PickTask>("Draft", new Dictionary<string, object?>
        {
            ["FromCell"] = storage.Value,
        });
        foreach (var line in order.Lines)
        {
            if (line.Quantity <= 0m || line.Item == Guid.Empty) continue;
            task.Lines.Add(new PickTaskLinesTablePartRow
            {
                Item = line.Item,
                Quantity = line.Quantity,
                ToCell = order.Location,
            });
        }
        if (task.Lines.Count == 0) return Guid.Empty;

        await _documents.SaveDocumentAsync(task);
        await _documents.AddLinkAsync(order.MetaId, task.MetaId);
        return task.MetaId;
    }

    private async Task<Guid?> LinkedChildAsync(Guid orderId, Guid docType)
    {
        var family = await _documents.GetDocumentFamilyAsync(orderId);
        var ids = new HashSet<Guid>(family.Nodes.Where(n => n.DocTypeMetaId == docType).Select(n => n.DocId));
        var edge = family.Edges.FirstOrDefault(e => e.ParentDocId == orderId && ids.Contains(e.ChildDocId));
        return edge?.ChildDocId;
    }

    /// <summary>Empty and Extended both mean the warehouse picks by hand.
    /// An unset enum is zero, and zero must not start moving stock.</summary>
    private static async Task<WarehouseFulfillmentScheme> SchemeAsync()
    {
        var row = (await ScriptServices.Get<IDictionaryManager<InventorySettings>>().GetRecordsAsync("1 = 1"))
            .FirstOrDefault();
        return row?.FulfillmentScheme ?? WarehouseFulfillmentScheme.Unspecified;
    }

    /// <summary>Storage cells of the store. Index 0 is the cell a one-cell
    /// pick already used, so an order that fits in it still makes one task.</summary>
    private async Task<List<Guid>> StorageCellsInPickOrderAsync(Guid store)
    {
        var ordered = new List<Guid>();
        var first = await Cells.SuggestStorageCellAsync(store);
        if (first is Guid head && head != Guid.Empty) ordered.Add(head);
        foreach (var cell in await Cells.GetCellsOfStoreAsync(store))
        {
            if (cell == Guid.Empty || ordered.Contains(cell)) continue;
            if (await Cells.IsCellAllowedForAsync(cell, StoreCellPurpose.Storage))
                ordered.Add(cell);
        }
        return ordered;
    }

    private async Task<decimal> FreeAtAsync(
        Dictionary<(Guid Cell, Guid Item), decimal> onHand, Guid cell, Guid item)
    {
        if (onHand.TryGetValue((cell, item), out var known)) return known;
        var dims = new Dictionary<string, object?> { ["Cell"] = cell, ["Item"] = item };
        var qty = await _totals.GetBalanceAsync("Stock", "Qty", dims)
                - await _totals.GetBalanceAsync("ReservedStock", "Qty", dims);
        if (qty < 0m) qty = 0m;
        onHand[(cell, item)] = qty;
        return qty;
    }

    private static void AddPick(
        Dictionary<Guid, Dictionary<Guid, decimal>> buckets, Guid cell, Guid item, decimal qty)
    {
        if (qty <= 0m) return;
        if (!buckets.TryGetValue(cell, out var lines))
        {
            lines = new Dictionary<Guid, decimal>();
            buckets[cell] = lines;
        }
        lines[item] = lines.TryGetValue(item, out var already) ? already + qty : qty;
    }

    /// <summary>Issue an invoice for the order. Empty Guid — no lines to ship
    /// or the order was not found. An already-issued invoice is returned as-is.</summary>
    public async Task<Guid> InvoiceOrderAsync(Guid orderId)
    {
        if (orderId == Guid.Empty) return Guid.Empty;

        var existing = await _documents.CountDocumentsAsync<SalesRealization>(
            $"SourceOrder = '{orderId}'");
        if (existing > 0)
        {
            var found = (await _documents.QueryDocumentsAsync<SalesRealization>(
                $"SourceOrder = '{orderId}'")).FirstOrDefault();
            return found?.MetaId ?? Guid.Empty;
        }

        var order = await _documents.GetDocumentAsync<SalesOrder>(orderId);
        if (order == null || order.Lines.Count == 0) return Guid.Empty;

        var invoice = await _documents.NewDocumentAsync<SalesRealization>();
        invoice.Customer = order.Customer;
        invoice.Location = order.Location;
        invoice.SourceOrder = order.MetaId;
        if (order.Outlet != Guid.Empty)
            invoice.Outlet = order.Outlet;
        if (order.Contract != Guid.Empty)
            invoice.Contract = order.Contract;
        if (order.DeliveryDate != default)
            invoice.DocumentDate = order.DeliveryDate.Date;
        if (order.Contact != Guid.Empty)
            invoice.Contact = order.Contact;
        if (order.PaymentTerm != Guid.Empty)
            invoice.PaymentTerm = order.PaymentTerm;
        if (order.DeliveryTerm != Guid.Empty)
            invoice.DeliveryTerm = order.DeliveryTerm;
        if (order.DiscountPercent != 0m)
            invoice.DiscountPercent = order.DiscountPercent;

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
        await _posting.SetSubtypeAsync(SalesInvoiceType, invoice.MetaId, "Reserved");
        await _documents.AddLinkAsync(order.MetaId, invoice.MetaId);
        return invoice.MetaId;
    }

    /// <summary>Invoice issued (Issued) — the source order becomes Delivered.
    /// Call after the invoice SaveDocumentAsync, not from OnAfterPost: a nested
    /// SetSubtypeAsync is swallowed by the platform there.</summary>
    public async Task MarkSourceOrderDeliveredAsync(Guid invoiceId)
    {
        if (invoiceId == Guid.Empty) return;
        var invoice = await _documents.GetDocumentAsync<SalesRealization>(invoiceId);
        var sourceOrder = invoice?.SourceOrder ?? Guid.Empty;
        if (sourceOrder == Guid.Empty) return;

        var order = await _documents.GetDocumentAsync<SalesOrder>(sourceOrder);
        if (order is not null && order.Subtype == SalesOrder.Subtypes.Confirmed)
            await _posting.SetSubtypeAsync(SalesOrderType, sourceOrder, SalesOrder.Subtypes.Delivered);
    }

    /// <summary>Close trip stops: refused → Cancelled, otherwise Delivered
    /// (the invoice is set by the order handler). Repeat-safe.</summary>
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

            if (stop.Outcome == StopOutcome.Refused)
            {
                if (order.Subtype == "Confirmed")
                    await _posting.SetSubtypeAsync(SalesOrderType, order.MetaId, "Cancelled");
                continue;
            }

            if (stop.Outcome == StopOutcome.Partial
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
