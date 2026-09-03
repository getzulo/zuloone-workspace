#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение Tax моделью GLIntegration: налог из расчёта становится проводкой
// в главной книге. Исходящий — обязательство, входящий — актив.
//
// ═══ ПОЧЕМУ ИМЕННО ЭТОТ ДОКУМЕНТ ИСТОЧНИК ИСТИНЫ ════════════════════════════
//
// Налог в системе считают ДВА независимых механизма: универсальный движок правил
// (этот документ → регистр TaxLedger) и страновой контур КСА (SaudiVatTx →
// регистр VatPayable). В книгу разносится ТОЛЬКО первый.
//
// Причина не в предпочтениях: страновой скрипт сам объявляет себя срезом для
// отчётности ZATCA поверх универсального контура. Провести оба — удвоить
// обязательство по НДС в главной книге на ровном месте. Универсальный движок
// выбран источником истины потому, что он общий: он работает в любой стране, а
// страновой контур существует не везде.
//
// ═══ ИСХОДЯЩИЙ: ПОЧЕМУ ДЕБЕТУЕТСЯ ДЕБИТОРКА ═════════════════════════════════
//
// Счёт продажи разносит Dr дебиторка / Cr выручка на сумму БЕЗ налога — и
// регистр Receivable тоже ведётся без него. Полная проводка продажи с НДС
// выглядит как Dr дебиторка (с налогом) / Cr выручка (без) / Cr НДС. Эта
// проводка добавляет недостающие две ноги: доводит дебиторку до суммы с налогом
// и создаёт обязательство. Итог тот же, что у трёхногой проводки, но счёт
// продажи трогать не пришлось.
//
// ═══ ВХОДЯЩИЙ: ЗЕРКАЛО, БЕЗ ВОЗМЕСТИМОСТИ ═══════════════════════════════════
//
// Заказ поставщику разносит Dr запасы / Cr кредиторка на сумму БЕЗ налога.
// Входящий налог — Dr НДС к возмещению / Cr кредиторка: актив для зачёта с
// исходящим и долг поставщику до суммы с налогом. Невозместимую часть в
// стоимость запаса не раскладываем: справочника возместимости нет, весь
// входящий налог считается возмещаемым. Когда возместимость появится — её
// место на TaxCode, не отдельная проводка здесь.
public partial class TaxCalculationGLEventHandler : TypedDocumentEventHandler<TaxCalculation>
{
    private const string OutputDirection = "OUTPUT";
    private const string InputDirection = "INPUT";
    private const string TaxCircuits = "FIN,TAX";

    public override async Task<EventResult> OnAfterPostAsync(TaxCalculation document, EventContext context)
    {
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

        // Направление строки — ССЫЛКА на справочник TaxDirection, а не строка:
        // сравнивать надо резолвнутый Code, как это делает TaxReturnService.
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

        // Юрлицо у расчёта в шапке: его зафиксировал документ-источник.
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

    // Платёж на сумму с налогом закрывает регистр; книга уже содержит налог
    // отдельной проводкой. Receivable/Payable ведутся без налога, GL AR/AP —
    // с налогом. Без этой ноги оплата гросса оставляла регистр на −налог.
    // OnAfterPost может прийти дважды — движение пишем только если его ещё нет.
    //
    // Связь invoice→calc кладётся ПОСЛЕ CreateCalculationAsync (он уже провёл
    // расчёт), поэтому в момент этого события граф семьи ещё пуст. Фоллбэк —
    // DeterminationReason («Sales invoice {Number}» / «Purchase order {Number}»).
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
                    // ITotalsManager.PostMovementAsync аналитики не принимает —
                    // Customer на Receivable динамический, иначе «Analytic required».
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
        string? number = null;
        if (reason.StartsWith(salesPrefix, StringComparison.Ordinal))
            number = reason[salesPrefix.Length..];
        else if (reason.StartsWith(purchasePrefix, StringComparison.Ordinal))
            number = reason[purchasePrefix.Length..];
        if (string.IsNullOrWhiteSpace(number)) return null;

        var escaped = number.Replace("'", "''");
        var invoices = await docs.QueryDocumentsAsync<SalesInvoice>($"ID = '{escaped}'");
        if (invoices.Count > 0) return invoices[0].MetaId;
        var orders = await docs.QueryDocumentsAsync<PurchaseOrder>($"ID = '{escaped}'");
        if (orders.Count > 0) return orders[0].MetaId;
        return null;
    }
}
