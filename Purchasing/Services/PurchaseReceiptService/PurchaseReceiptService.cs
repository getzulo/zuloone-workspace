#nullable enable
using System;
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

    /// <summary>Ordered → Received. Null = success.</summary>
    public async Task<string?> ReceiveAsync(Guid orderId)
    {
        var error = await ValidateReceiveAsync(orderId);
        if (error != null) return error;

        var full = await _documents.GetDocumentAsync<PurchaseOrder>(orderId);
        if (full == null) return "Заказ не найден.";

        full.Subtype = PurchaseOrder.Subtypes.Received;
        await _documents.SaveDocumentAsync(full);
        return null;
    }
}
