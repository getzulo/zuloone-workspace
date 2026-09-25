#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Managers;
using ZuloOne.Runtime;
using ZuloOne.Runtime.Generated;
using ZuloOne.Services.Contracts;

// Shared receive path for ReceiveOrder and for PlaceOrder when
// PurchasingSettings.AutoReceiveOnOrder is on. Draft → Received is still
// forbidden: the caller must already be Ordered (or PlaceOrder saves Ordered
// first). Contract takes Guid only — no generated entities on IFoo.
public partial class PurchaseReceiptService
{
    private readonly IDocumentManager _documents;

    public PurchaseReceiptService(IDocumentManager documents)
        => _documents = documents;

    // Inventory / Tax are other models — not in the constructor (factory
    // would fail to start). Same as SalesFulfillmentService / IStoreCellService.
    private static IStoreCellService Cells => ScriptServices.Get<IStoreCellService>();
    private static ITaxService Tax => ScriptServices.Get<ITaxService>();

    /// <summary>Null = the order can move to Received. Used by PlaceOrder
    /// before it leaves Draft, so a blocked receipt does not leave Ordered.</summary>
    public async Task<string?> ValidateReceiveAsync(Guid orderId)
    {
        var full = await _documents.GetDocumentAsync<PurchaseOrder>(orderId);
        if (full == null) return "Заказ не найден.";
        if (full.Lines.Count == 0)
            return "Нельзя принять пустой заказ: добавьте строки.";
        if (full.Lines.Any(l => l.Quantity <= 0m))
            return "В каждой строке количество должно быть больше нуля.";

        if (!await Cells.IsCellAllowedForAsync(full.Location, StoreCellPurpose.Receiving))
            return "Приход оформляется в ячейку ПРИЁМКИ — у выбранной ячейки другое назначение.";

        var taxPoint = full.DocumentDate == default ? DateTime.UtcNow.Date : full.DocumentDate.Date;
        var taxCode = await Tax.ResolveDefaultTaxCodeAsync();
        if (taxCode is not null && await Tax.ResolveRateAsync(taxCode.Value, taxPoint) is null)
            return $"Налоговый код настроен, но действующей ставки на {taxPoint:yyyy-MM-dd} нет — приход не проводится.";

        return null;
    }

    /// <summary>Ordered → Received. Null = success. A positive ReceiveQty
    /// on any line receives only that much; the rest becomes a new Ordered order.</summary>
    public async Task<string?> ReceiveAsync(Guid orderId)
    {
        var error = await ValidateReceiveAsync(orderId);
        if (error != null) return error;

        var full = await _documents.GetDocumentAsync<PurchaseOrder>(orderId);
        if (full == null) return "Заказ не найден.";

        var split = await SplitRemainderAsync(full);
        if (split != null) return split;

        full.Subtype = PurchaseOrder.Subtypes.Received;
        await _documents.SaveDocumentAsync(full);
        return null;
    }

    /// <summary>
    /// 0 на каждой строке — принять всё, поле ни на что не влияет.
    /// Иначе «Принять сейчас» — это количество этой приёмки. Ноль на строке,
    /// когда на другой строке количество задано, оставляет строку целиком
    /// на остаточном заказе. Остаток пишется новым заказом в Ordered и
    /// связывается с этим: склад и кредиторка по нему не двигаются, пока
    /// его тоже не примут.
    /// </summary>
    private async Task<string?> SplitRemainderAsync(PurchaseOrder order)
    {
        foreach (var line in order.Lines)
        {
            if (line.ReceiveQty < 0m)
                return "«Принять сейчас» не бывает меньше нуля.";
            if (line.ReceiveQty > line.Quantity)
                return "«Принять сейчас» больше количества строки.";
        }
        if (order.Lines.All(l => l.ReceiveQty <= 0m)) return null;

        var rest = new List<PurchaseOrderLinesTablePartRow>();
        for (var i = order.Lines.Count - 1; i >= 0; i--)
        {
            var line = order.Lines[i];
            if (line.ReceiveQty <= 0m)
            {
                rest.Insert(0, CopyLine(line, line.Quantity));
                order.Lines.RemoveAt(i);
                continue;
            }
            var leftover = line.Quantity - line.ReceiveQty;
            line.Quantity = line.ReceiveQty;
            line.ReceiveQty = 0m;
            if (leftover > 0m)
                rest.Insert(0, CopyLine(line, leftover));
        }
        if (order.Lines.Count == 0)
            return "Нечего принимать.";

        await _documents.SaveDocumentAsync(order);
        if (rest.Count == 0) return null;

        var remainder = await _documents.NewDocumentAsync<PurchaseOrder>();
        remainder.Supplier = order.Supplier;
        remainder.Location = order.Location;
        remainder.DocumentDate = order.DocumentDate;
        if (order.LegalEntity != Guid.Empty) remainder.LegalEntity = order.LegalEntity;
        if (order.PaymentTerm != Guid.Empty) remainder.PaymentTerm = order.PaymentTerm;
        if (order.ExpectedReceipt.Year >= 1902) remainder.ExpectedReceipt = order.ExpectedReceipt;
        foreach (var line in rest)
            remainder.Lines.Add(line);
        await _documents.SaveDocumentAsync(remainder);
        remainder.Subtype = PurchaseOrder.Subtypes.Ordered;
        await _documents.SaveDocumentAsync(remainder);
        await _documents.AddLinkAsync(order.MetaId, remainder.MetaId);
        return null;
    }

    private static PurchaseOrderLinesTablePartRow CopyLine(PurchaseOrderLinesTablePartRow source, decimal qty)
        => new()
        {
            Item = source.Item,
            Quantity = qty,
            Unit = source.Unit,
            UnitPrice = source.UnitPrice,
        };
}
