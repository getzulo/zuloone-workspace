using System;
#nullable enable
using System.Linq;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Validate a vendor payment before posting. Payable is created with
// allowNegativeBalance=true — overpayment and advances to the vendor are lawful,
// so there is no engine cutoff on the balance and there must not be one; only
// the document itself is checked: an empty payment and a non-positive amount.
//
// Lines are re-read via IDocumentManager: the header event does not carry the
// table part (the same pattern as PurchaseOrder and ProductionOrder).
public partial class VendorPaymentEventHandler : TypedDocumentEventHandler<VendorPayment>
{
    public override async Task<EventResult> OnBeforeCreateAsync(VendorPayment header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("VendorPayment");
        if (header.LegalEntity == Guid.Empty)
        {
            var createId = createDefaults.Pick(createSeed, "LegalEntity");
            if (createId != Guid.Empty) header.LegalEntity = createId;
        }
        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforePostAsync(VendorPayment document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Paid")
            return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<VendorPayment>(document.MetaId);
        var lines = full?.Lines ?? document.Lines;

        if (lines.Count == 0)
            return EventResult.Cancel("Заполните строки оплаты");

        if (lines.Any(l => l.Amount <= 0m))
            return EventResult.Cancel("Сумма оплаты должна быть больше нуля");

        return EventResult.Ok();
    }
}
