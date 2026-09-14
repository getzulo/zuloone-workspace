#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// GLIntegration extension of Tax: tax from the calculation becomes a journal
// entry in the general ledger. Output — a liability, input — an asset.
//
// ═══ WHY THIS DOCUMENT IS THE SOURCE OF TRUTH ═══════════════════════════════
//
// Tax in the system is computed by TWO independent mechanisms: the universal
// rule engine (this document → TaxLedger register) and the KSA country contour
// (SaudiVatTx → VatPayable register). ONLY the first is posted to the books.
//
// The reason is not preference: the country script itself declares that it is
// a slice for ZATCA reporting on top of the universal contour. Posting both
// would double the VAT liability in the general ledger for no reason. The
// universal engine is the source of truth because it is shared: it works in
// any country, while the country contour does not exist everywhere.
//
// ═══ OUTPUT: WHY RECEIVABLES ARE DEBITED ════════════════════════════════════
//
// The sales invoice posts Dr receivables / Cr revenue on the amount WITHOUT
// tax — and the Receivable register is also kept without it. The full sales
// posting with VAT looks like Dr receivables (with tax) / Cr revenue (without)
// / Cr VAT. This posting adds the two missing legs: it brings receivables up
// to the tax-inclusive amount and creates the liability. The result is the
// same as a three-leg posting, but the sales account did not have to be
// touched.
//
// ═══ INPUT: THE MIRROR, WITHOUT RECOVERABILITY ══════════════════════════════
//
// A purchase order posts Dr inventory / Cr payables on the amount WITHOUT tax.
// Input tax is Dr VAT recoverable / Cr payables: an asset to offset against
// output and a supplier debt up to the tax-inclusive amount. The non-recoverable
// portion is not folded into inventory cost: there is no recoverability
// dictionary, so all input tax is treated as recoverable. When recoverability
// arrives, it belongs on TaxCode, not as a separate posting here.
public partial class TaxCalculationGLEventHandler : TypedDocumentEventHandler<TaxCalculation>
{
    private const string OutputDirection = "OUTPUT";
    private const string InputDirection = "INPUT";
    private const string TaxCircuits = "FIN,TAX";

    public override async Task<EventResult> OnAfterPostAsync(TaxCalculation document, EventContext context){
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        if (document.Subtype != "Finalized") return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        foreach (var jeId in await PostToLedgerAsync(document, context))
            await docs.AddLinkAsync(document.MetaId, jeId);

        return EventResult.Ok();
    }

    private async Task<List<Guid>> PostToLedgerAsync(TaxCalculation header, EventContext context)
    {
        var posted = new List<Guid>();

        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return posted;

        var calc = await context.GetService<IDocumentManager>().GetDocumentAsync<TaxCalculation>(header.MetaId);
        if (calc == null) return posted;

        // Line direction is a REFERENCE to the TaxDirection dictionary, not a string:
        // compare the resolved Code, the same way TaxReturnService does.
        var directions = context.GetService<IDictionaryManager<TaxDirection>>();
        var output = 0m;
        var input = 0m;
        foreach (var line in calc.Lines)
        {
            var code = (await directions.GetRecordAsync(line.Direction))?.Code;
            if (string.Equals(code, OutputDirection, StringComparison.OrdinalIgnoreCase))
                output += line.TaxAmount;
            else if (string.Equals(code, InputDirection, StringComparison.OrdinalIgnoreCase))
                input += line.TaxAmount;
        }
        if (output <= 0m && input <= 0m) return posted;

        // The calculation carries the legal entity on the header: the source document pinned it.
        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(header.LegalEntity);
        if (le == null) return posted;

        if (output > 0m && !string.IsNullOrWhiteSpace(settings.VatPayableAccountCode))
        {
            var jeId = await gl.PostAsync(
                calc.DocumentDate, le.MetaId, le.Currency, output,
                settings.ArAccountCode, settings.VatPayableAccountCode,
                "Output VAT " + header.MetaId,
                "Дебиторка (налог с покупателя)", "НДС к уплате",
                TaxCircuits);
            if (jeId.HasValue) posted.Add(jeId.Value);
        }

        if (input > 0m
            && !string.IsNullOrWhiteSpace(settings.VatReceivableAccountCode)
            && !string.IsNullOrWhiteSpace(settings.PayableAccountCode))
        {
            var jeId = await gl.PostAsync(
                calc.DocumentDate, le.MetaId, le.Currency, input,
                settings.VatReceivableAccountCode, settings.PayableAccountCode,
                "Input VAT " + header.MetaId,
                "НДС к возмещению", "Кредиторка (налог поставщику)",
                TaxCircuits);
            if (jeId.HasValue) posted.Add(jeId.Value);
        }

        await CloseReceivablePayableSeamAsync(header, calc, output, input, context);
        return posted;
    }

    // A payment for the tax-inclusive amount closes the register; the books
    // already hold the tax as a separate posting. Receivable/Payable are kept
    // without tax, GL AR/AP — with tax. Without this leg, a gross payment
    // left the register at −tax.
    // OnAfterPost may fire twice — write the movement only if it is not there yet.
    //
    // The invoice→calc link is written AFTER CreateCalculationAsync (it has
    // already posted the calculation), so at this event the family graph is
    // still empty. Fallback — DeterminationReason («Sales invoice {Number}» /
    // «Purchase order {Number}»).
    private static async Task CloseReceivablePayableSeamAsync(
        TaxCalculation header, TaxCalculation calc, decimal output, decimal input, EventContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var family = await docs.GetDocumentFamilyAsync(header.MetaId);
        var parent = family.Edges.FirstOrDefault(e => e.ChildDocId == header.MetaId)?.ParentDocId;
        if (parent is null || parent == Guid.Empty)
            parent = await ResolveSourceAsync(docs, calc);
        if (parent is null || parent == Guid.Empty) return;

        var totals = context.GetService<ITotalsManager>();
        var movements = context.GetService<IRegisterMovementService>();
        var metadata = context.GetService<IMetadataService>();
        var registers = await metadata.GetAllRegistersAsync();

        if (output > 0m)
        {
            var invoice = await docs.GetDocumentAsync<SalesInvoice>(parent.Value);
            if (invoice != null && invoice.Customer != Guid.Empty)
            {
                var existing = await totals.QueryMovementsAsync(
                    "Receivable", $"[DocumentMetaId] = '{header.MetaId}'");
                if (existing.Count == 0)
                {
                    // Customer on Receivable is a dynamic analytic, not a TB_ column.
                    var receivableId = registers.First(r =>
                        string.Equals(r.Name, "Receivable", StringComparison.OrdinalIgnoreCase)).MetaId;
                    await movements.PostMovementAsync(receivableId, header.MetaId, calc.DocumentDate,
                        new Dictionary<string, object?>(),
                        new Dictionary<string, decimal> { ["Amount"] = output },
                        analytics: new Dictionary<string, object?> { ["Customer"] = invoice.Customer });
                }
            }
        }

        if (input > 0m)
        {
            var order = await docs.GetDocumentAsync<PurchaseOrder>(parent.Value);
            if (order != null && order.Supplier != Guid.Empty)
            {
                var existing = await totals.QueryMovementsAsync(
                    "Payable", $"[DocumentMetaId] = '{header.MetaId}'");
                if (existing.Count == 0)
                {
                    var payableId = registers.First(r =>
                        string.Equals(r.Name, "Payable", StringComparison.OrdinalIgnoreCase)).MetaId;
                    await movements.PostMovementAsync(payableId, header.MetaId, calc.DocumentDate,
                        new Dictionary<string, object?>(),
                        new Dictionary<string, decimal> { ["Amount"] = input },
                        analytics: new Dictionary<string, object?> { ["Supplier"] = order.Supplier });
                }
            }
        }
    }

    private static async Task<Guid?> ResolveSourceAsync(IDocumentManager docs, TaxCalculation calc)
    {
        var reason = calc.DeterminationReason;
        if (string.IsNullOrWhiteSpace(reason)) return null;

        const string salesPrefix = "Sales invoice ";
        const string purchasePrefix = "Purchase order ";
        var escaped = (string n) => n.Replace("'", "''");
        if (reason.StartsWith(salesPrefix, StringComparison.Ordinal))
        {
            var number = reason[salesPrefix.Length..];
            if (string.IsNullOrWhiteSpace(number)) return null;
            var invoices = await docs.QueryDocumentsAsync<SalesInvoice>($"ID = '{escaped(number)}'");
            if (invoices.Count > 0) return invoices[0].MetaId;
        }
        else if (reason.StartsWith(purchasePrefix, StringComparison.Ordinal))
        {
            var number = reason[purchasePrefix.Length..];
            if (string.IsNullOrWhiteSpace(number)) return null;
            var orders = await docs.QueryDocumentsAsync<PurchaseOrder>($"ID = '{escaped(number)}'");
            if (orders.Count > 0) return orders[0].MetaId;
        }
        return null;
    }
}
