#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// HR extension: a PAYROLL PAYOUT is posted to the general ledger —
// Dr employee payable / Cr cash.
//
// Why: accrual credits the liability account (PayrollGLEventHandler), and
// there was nothing to debit it with — in the PayrollLiability register the
// debt was cleared by PayrollPaymentTx, while the ledger grew without bound.
// This pair closes the gap: after the payout the GL liability account matches
// the register balance.
//
// The payout does not carry a legal entity on the header (only ID), so the
// chain is longer than on the accrual: Line → Employee → Division → LegalEntity.
// A payout may cover employees of DIFFERENT legal entities, so amounts are
// grouped by legal entity and each gets its own posting — one shared entry
// would distort both books.
//
// The class name is deliberately different from PayrollGLEventHandler: script
// class names are unique across the whole workspace, and a collision would
// displace the accrual handler.
public partial class PayrollPaymentGLEventHandler : TypedDocumentEventHandler<PayrollPayment>
{
    public override async Task<EventResult> OnAfterPostAsync(PayrollPayment document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Paid") return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        foreach (var jeId in await PostToLedgerAsync(document, context))
            await docs.AddLinkAsync(document.MetaId, jeId);

        return EventResult.Ok();
    }

    private async Task<List<Guid>> PostToLedgerAsync(PayrollPayment header, EventContext context)
    {
        var posted = new List<Guid>();

        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return posted;

        var payment = await context.GetService<IDocumentManager>().GetDocumentAsync<PayrollPayment>(header.MetaId);
        if (payment == null) return posted;

        var employees = context.GetService<IDictionaryManager<Employee>>();
        var divisions = context.GetService<IDictionaryManager<Division>>();
        var entities = context.GetService<IDictionaryManager<LegalEntity>>();

        // Amount to post — by the employee's legal entity, not the document total.
        var byLegalEntity = new Dictionary<Guid, decimal>();
        foreach (var line in payment.Lines)
        {
            if (line.Amount <= 0m) continue;

            var emp = await employees.GetRecordAsync(line.Employee);
            if (emp == null) continue;
            var div = await divisions.GetRecordAsync(emp.Division);
            if (div == null) continue;

            byLegalEntity[div.LegalEntity] =
                (byLegalEntity.TryGetValue(div.LegalEntity, out var acc) ? acc : 0m) + line.Amount;
        }

        foreach (var kv in byLegalEntity)
        {
            if (kv.Value <= 0m) continue;

            var le = await entities.GetRecordAsync(kv.Key);
            if (le == null) continue;

            // The description carries both the document and the legal entity:
            // GeneralLedgerService idempotency is built on it, and one payout
            // may produce several postings.
            var jeId = await gl.PostAsync(
                payment.DocumentDate, le.MetaId, le.Currency, kv.Value,
                settings.PayrollLiabilityAccountCode, settings.CashAccountCode,
                $"Payroll payment {header.MetaId} / {le.MetaId}",
                "Задолженность перед сотрудниками", "Денежные средства");

            if (jeId.HasValue) posted.Add(jeId.Value);
        }

        return posted;
    }
}
