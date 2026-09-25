#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for SalesRealization documents.
// `header` is a typed SalesRealization entity — access fields directly (header.Number).
// Record events (insert/update/delete/validate) plus the document-only posting
// events are stubbed below. Cancel a transition with EventResult.Cancel("reason");
// document table-part rows are available via document.TableParts["Name"].
public partial class SalesInvoiceEventHandler : TypedDocumentEventHandler<SalesRealization>
{
    // Building a new document server-side: seed header defaults (number, date).
    // Copy-defaults from Customer are applied in OnBeforeSaveAsync(isNew=true) below.
    public override async Task<EventResult> OnBeforeCreateAsync(SalesRealization header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        // RecordDefaults: валюта, юрлицо, ячейка, срок и даты начала — из настроек, пока поле пустое.
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("SalesRealization");
        if (header.LegalEntity == Guid.Empty)
        {
            var createId = createDefaults.Pick(createSeed, "LegalEntity");
            if (createId != Guid.Empty) header.LegalEntity = createId;
        }
        if (header.PaymentTerm == Guid.Empty)
        {
            var createId = createDefaults.Pick(createSeed, "PaymentTerm");
            if (createId != Guid.Empty) header.PaymentTerm = createId;
        }
        if (header.DueDate.Year < 1902)
        {
            var createDay = createDefaults.PickDay(createSeed, "DueDate");
            if (createDay.Year >= 1902) header.DueDate = createDay;
        }
        return EventResult.Ok();
    }

    // MIQS BeforeSave: runs before ANY save — insert (isNew) or update.
    // DiscountPercent outside [0, 100] flips LineAmount (>100%) or is a markup
    // (<0), not a discount. Catch both here, not only at LoyaltyTier: the field
    // is also typed by hand on the document form.
    // On a subtype-only write (SetSubtypeAsync) header is a partial instance and
    // DiscountPercent is zero (see SalesInvoiceLoyaltyDiscountHandler) — still
    // in range, so the check is harmless.
    public override async Task<EventResult> OnBeforeSaveAsync(SalesRealization header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;
        if (header.DiscountPercent < 0m || header.DiscountPercent > 100m)
            return EventResult.Cancel("Скидка на счёте должна быть в диапазоне от 0 до 100%");
        if (!isNew)
            await MergeStoredHeaderAsync(header, context);

        var onDate = header.DocumentDate != default ? header.DocumentDate : DateTime.UtcNow;
        var contracts = context.GetService<ISalesContractService>();
        var stamp = await contracts.ResolveStampAsync(
            header.Customer, header.Outlet, header.Contract, onDate);
        var createDefaults = context.GetService<IRecordDefaults>();
        var createSeed = await createDefaults.SeedAsync("SalesRealization");
        var moduleTerm = createDefaults.Pick(createSeed, "PaymentTerm");
        var moduleEntity = createDefaults.Pick(createSeed, "LegalEntity");
        if (header.Customer == Guid.Empty && stamp.TryGetValue("Customer", out var c) && c is Guid stampedCustomer)
            header.Customer = stampedCustomer;
        if (header.Outlet == Guid.Empty && stamp.TryGetValue("Outlet", out var o) && o is Guid stampedOutlet)
            header.Outlet = stampedOutlet;
        if (header.Contract == Guid.Empty && stamp.TryGetValue("Contract", out var k) && k is Guid stampedContract)
            header.Contract = stampedContract;
        if (createDefaults.IsPlaceholder(header.PaymentTerm, moduleTerm)
            && stamp.TryGetValue("PaymentTerm", out var p) && p is Guid term && term != Guid.Empty)
            header.PaymentTerm = term;
        if (header.DeliveryTerm == Guid.Empty && stamp.TryGetValue("DeliveryTerm", out var d) && d is Guid delivery)
            header.DeliveryTerm = delivery;
        if (header.Contact == Guid.Empty && stamp.TryGetValue("Contact", out var n) && n is Guid stampedContact)
            header.Contact = stampedContact;
        if (createDefaults.IsPlaceholder(header.LegalEntity, moduleEntity)
            && stamp.TryGetValue("LegalEntity", out var le) && le is Guid legal && legal != Guid.Empty)
            header.LegalEntity = legal;

        var pair = await contracts.ValidatePairAsync(
            header.Customer, header.Outlet, header.Contract, onDate);
        if (pair != null)
            return EventResult.Cancel(pair);

        // Copy PaymentTerm and primary Contact from Customer when still empty.
        if (header.Customer != Guid.Empty)
        {
            var dm = context.GetService<IDictionaryManager<Customer>>();
            var customer = await dm.GetRecordAsync(header.Customer);
            if (customer is not null)
            {
                if (createDefaults.IsPlaceholder(header.PaymentTerm, moduleTerm) && customer.PaymentTerm != Guid.Empty)
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

        await PriceDraftLinesAsync(header, context);
        await StampDueDateAsync(header, context);
        return EventResult.Ok();
    }

    private static async Task StampDueDateAsync(SalesRealization header, EventContext context)
    {
        var due = context.GetService<IPaymentDueService>();
        var days = header.PaymentTerm != Guid.Empty
            ? await due.DaysOfAsync(header.PaymentTerm)
            : await DefaultDaysAsync(context);
        header.DueDate = due.DueOn(header.DocumentDate, days);
    }

    private static async Task<int> DefaultDaysAsync(EventContext context)
    {
        var rows = await context.GetService<IDictionaryManager<SalesSettings>>()
            .GetRecordsAsync("1 = 1");
        return rows.Count > 0 ? rows[0].DefaultPaymentTermDays : 0;
    }

    private static bool IsDraft(string? subtype)
        => string.IsNullOrEmpty(subtype)
           || string.Equals(subtype, "Draft", StringComparison.Ordinal);

    // Table-part persist hooks do not see the header, so contract type would
    // fall through to the customer. Draft save is the place that has both.
    private static async Task PriceDraftLinesAsync(SalesRealization header, EventContext context)
    {
        if (!IsDraft(header.Subtype) || header.Lines == null || header.Lines.Count == 0)
            return;
        var fill = context.GetService<ISalesLinePricing>();
        var onDate = header.DocumentDate != default ? header.DocumentDate : DateTime.UtcNow;
        foreach (var line in header.Lines)
        {
            var applied = await fill.ApplyFieldAsync(
                line.Unit == Guid.Empty ? "Item" : "Quantity",
                line.Item, line.Unit, line.Quantity, line.UnitPrice, line.PriceExplanation,
                header.Customer, header.Contract, onDate);
            if (applied.TryGetValue("Unit", out var u) && u is Guid unit) line.Unit = unit;
            if (applied.TryGetValue("Quantity", out var q) && q != null) line.Quantity = Convert.ToDecimal(q);
            if (applied.TryGetValue("UnitPrice", out var p) && p != null) line.UnitPrice = Convert.ToDecimal(p);
            if (applied.TryGetValue("PriceExplanation", out var e))
                line.PriceExplanation = e as string ?? "";
        }
    }

    private static async Task MergeStoredHeaderAsync(SalesRealization header, EventContext context)
    {
        if (header.MetaId == Guid.Empty) return;
        var stored = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesRealization>(header.MetaId);
        if (stored is null) return;
        if (header.Customer == Guid.Empty) header.Customer = stored.Customer;
        if (header.Outlet == Guid.Empty) header.Outlet = stored.Outlet;
        if (header.Contract == Guid.Empty) header.Contract = stored.Contract;
        if (header.Location == Guid.Empty) header.Location = stored.Location;
        if (header.LegalEntity == Guid.Empty) header.LegalEntity = stored.LegalEntity;
        if (header.DocumentDate == default) header.DocumentDate = stored.DocumentDate;
        if (header.PaymentTerm == Guid.Empty) header.PaymentTerm = stored.PaymentTerm;
    }

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(SalesRealization header, bool isNew, EventContext context)
        => next(header, isNew, context);

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(SalesRealization header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(SalesRealization header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(SalesRealization header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(SalesRealization header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before the document is deleted.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // After the document was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => next(recordId, context);

    // Before posting: reject overselling — a line cannot ship more than is on hand
    // at the sale location. Stock is a single-entry register (allowNegativeBalance:true),
    // so the engine does not guard this; the check lives here (reads on-hand via
    // IRegisterMovementService.GetBalanceAsync on the physical Item+Cell dimensions).
    // Note: check-then-act, not atomic with posting.
    private static readonly Guid StockRegister = Guid.Parse("83559331-ac7f-46da-87a8-7da599ef6f41");

    /// <summary>Document type — target of the header-only update.</summary>
    private static readonly Guid SalesInvoiceType = Guid.Parse("34a1af4c-aeaf-48d1-8626-9a0a13b2d5c3");

    public override async Task<EventResult> OnBeforePostAsync(SalesRealization header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        // The Paid subtype is unreachable on purpose and must stay locked.
        //
        // The subtype still exists (historical documents and reports), but the
        // Issued→Paid edge is gone from the map — the form never offers it.
        // This lock covers a direct API call. Issued carries stock, revenue,
        // receivable (Sales), loyalty points (CRM) and country VAT
        // (LocalizationSaudiArabia). Leaving it would reverse those movements —
        // debt with no payment, points, and the tax liability. Paid is
        // isReadOnly; there would be no way back.
        //
        // Payment here is a SEPARATE document (CustomerPayment) that settles
        // Receivable and does not touch the invoice. Payment status is read
        // from the register, not the subtype. The same lesson is in the
        // disabled MarkPaid command; here it is a lock, not only a comment.
        if (header.Subtype == "Paid")
            return EventResult.Cancel(
                "Счёт нельзя перевести в «Оплачен» вручную: это снимет дебиторку без оплаты, "
                + "баллы лояльности и начисленный НДС. Проведите оплату документом CustomerPayment — "
                + "он погасит долг, а счёт останется выставленным.");

        if (header.Subtype != "Issued") return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesRealization>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;

        // Address discipline: ship from a PICKING cell, where the pick task
        // put the goods. The check asks Inventory, it does not compare cell
        // type names. Discipline off (default) — any cell is fine, invoice
        // posts as before.
        if (!await context.GetService<IStoreCellService>()
                .IsCellAllowedForAsync(full?.Location ?? header.Location, StoreCellPurpose.Picking))
            return EventResult.Cancel(
                "Отгрузка идёт из ячейки ОТБОРА — у выбранной ячейки другое назначение");

        // The seller legal entity is STAMPED on the document, not resolved by
        // every consumer. Technical reason: the tax ledger is sliced by legal
        // entity, and the writer is a SYNCHRONOUS transaction script that
        // cannot walk Cell → Zone → Warehouse → Division → LegalEntity (four
        // async dictionary reads). On the posting path those reads are fine.
        //
        // Accounting reason is as strong: org structure will be re-parented
        // later; the invoice must remember WHO sold, not recompute today's
        // tree. A value typed by hand is not overwritten — ship cell and
        // seller do not always match.
        //
        // Written as a HEADER-ONLY update, not header.LegalEntity = …: the
        // event instance never reaches the database (IssueStampsSellingLegalEntity
        // failed on that). SaveDocumentAsync is also wrong here: it rewrites
        // EVERY line mid-posting.
        var current = full?.LegalEntity ?? header.LegalEntity;
        var sellerDefaults = context.GetService<IRecordDefaults>();
        var moduleSeller = sellerDefaults.Pick(await sellerDefaults.SeedAsync("SalesRealization"), "LegalEntity");
        if (sellerDefaults.IsPlaceholder(current, moduleSeller))
        {
            var resolved = await context.GetService<IStoreCellService>()
                .GetLegalEntityAsync(full?.Location ?? header.Location);
            if (resolved.HasValue)
            {
                header.LegalEntity = resolved.Value;
                current = resolved.Value;
                await context.GetService<IDocumentManager>().UpdateDocumentAsync(
                    SalesInvoiceType, header.MetaId,
                    new Dictionary<string, object?> { ["LegalEntity"] = resolved.Value });
            }
        }

        var issued = full ?? header;
        var contractSvc = context.GetService<ISalesContractService>();
        var onDate = issued.DocumentDate != default ? issued.DocumentDate : DateTime.UtcNow;
        var pair = await contractSvc.ValidatePairAsync(
            issued.Customer, issued.Outlet, issued.Contract, onDate);
        if (pair != null)
            return EventResult.Cancel(pair);
        if (issued.Contract != Guid.Empty)
        {
            var contract = await context.GetService<IDictionaryManager<SalesContract>>()
                .GetRecordAsync(issued.Contract);
            if (contract is not null)
            {
                var currencyError = await contractSvc.CheckCurrencyAsync(contract.Currency, current);
                if (currencyError != null)
                    return EventResult.Cancel(currencyError);
            }

            var pricing = context.GetService<IPricingService>();
            var amount = lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice, issued.DiscountPercent));
            var settlement = await contractSvc.CheckSettlementAsync(issued.Customer, issued.Contract, amount);
            if (settlement != null)
                return EventResult.Cancel(settlement);
        }

        // Compared to the register balance, which is in the item BASE unit —
        // so demand uses BaseQuantity. Zero means no unit was set, no convert.
        // Tax base below (OnAfterPostAsync) INTENTIONALLY stays on entered
        // Quantity: price is per entered unit (5 cases at the case price).
        var demand = new Dictionary<Guid, decimal>();
        foreach (var line in lines)
        {
            var qty = line.BaseQuantity != 0m ? line.BaseQuantity : line.Quantity;
            demand[line.Item] = (demand.TryGetValue(line.Item, out var d) ? d : 0m) + qty;
        }

        var stock = context.GetService<IRegisterMovementService>();
        foreach (var kv in demand)
        {
            var bal = await stock.GetBalanceAsync(StockRegister,
                new Dictionary<string, object?> { ["Item"] = kv.Key, ["Cell"] = header.Location });
            var onHand = bal is null ? 0m : Convert.ToDecimal(bal["Qty"]);
            if (kv.Value > onHand)
                return EventResult.Cancel($"Недостаточно остатка на ячейке: требуется {kv.Value}, в наличии {onHand}");
        }

        // Tax is CONFIGURED, but there is no rate on the invoice date — do not
        // issue. That is not "tax off" (then there is no default code and this
        // check stays quiet); it is a broken setup. An invoice that silently
        // left without VAT will show up at the tax authority.
        //
        // The check lives HERE, in a cancellable event, not next to calculation
        // create in OnAfterPost: the platform marks OnAfterPost non-cancellable
        // and turns a handler exception into a log warning — the document still
        // posts, and the missing tax is silent again.
        var tax = context.GetService<ITaxService>();
        var taxCode = await tax.ResolveDefaultTaxCodeAsync();
        if (taxCode is null) return EventResult.Ok();

        var taxPoint = TaxPointOf(header);
        var rate = await tax.ResolveRateAsync(taxCode.Value, taxPoint);
        if (rate is null)
            return EventResult.Cancel(
                $"Налоговый код настроен, но действующей ставки на {taxPoint:yyyy-MM-dd} нет — счёт не выставляется");

        // The rate is STAMPED on the document — same reason as legal entity:
        // country postings (KSA VAT into VatPayable) are synchronous and cannot
        // pick a dated rate themselves. Localization used to read a flat
        // SaudiVatRate constant with no date: any rate change split the country
        // register from TaxLedger, and a back-dated invoice used today's rate.
        // One source now: TaxRate, resolved on the invoice date with the same
        // call OnAfterPost will use for the calculation.
        //
        // Header-only update, not assignment: the event instance never reaches
        // the database (see LegalEntity). A hand-filled rate is kept — the
        // invoice may have been issued at an agreed rate.
        if (header.TaxRateApplied == 0m && (full?.TaxRateApplied ?? 0m) == 0m)
        {
            header.TaxRateApplied = rate.Value;
            await context.GetService<IDocumentManager>().UpdateDocumentAsync(
                SalesInvoiceType, header.MetaId,
                new Dictionary<string, object?> { ["TaxRateApplied"] = rate.Value });
        }

        return EventResult.Ok();
    }

    /// <summary>Tax-point date is the document date; empty is today, same as
    /// IDocumentManager stamps on create.</summary>
    private static DateTime TaxPointOf(SalesRealization header)
        => header.DocumentDate == default ? DateTime.UtcNow.Date : header.DocumentDate.Date;

    // An issued invoice creates an OUTPUT tax calculation: a separate
    // TaxCalculation document linked on the document graph. Separate, not a
    // field on the invoice: tax has its own ledger, reporting, and tax-point
    // date.
    //
    // Created here, not in the transaction script: rate and tax code are
    // async dictionary reads; GetTransactions is synchronous.
    /// <summary>Order type — target of the subtype write when fulfillment closes.</summary>
    private static readonly Guid SalesOrderType = Guid.Parse("23643b1b-b959-4206-83ab-948c713276c9");

    public override async Task<EventResult> OnAfterPostAsync(SalesRealization header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        if (header.Subtype != "Issued") return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var invoice = await docs.GetDocumentAsync<SalesRealization>(header.MetaId);
        if (invoice is null || invoice.Lines.Count == 0) return EventResult.Ok();

        // Seller legal entity is ALREADY stamped (OnBeforePost) — read it,
        // do not resolve again from the cell. Tax and the invoice then name
        // the same seller even if org structure is re-parented between post
        // and repost.
        var pricing = context.GetService<IPricingService>();
        var legalEntity = invoice.LegalEntity;
        if (legalEntity != Guid.Empty)
        {
            var taxBase = invoice.Lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice, invoice.DiscountPercent));

            // Contour is optional: not set — service returns null, invoice
            // issues as before. Rate is on the INVOICE DATE, not today:
            // otherwise invoice and tax would date differently, and a
            // back-dated document would use today's rate.
            var calc = await context.GetService<ITaxService>()
                .CreateCalculationAsync(legalEntity, "OUTPUT", taxBase, $"Sales invoice {header.MetaId:D}",
                    TaxPointOf(header), await TaxContextAsync(invoice, taxBase, context));
            if (calc.HasValue)
                await docs.AddLinkAsync(header.MetaId, calc.Value);
        }

        // Close the source order: invoice issued → order Delivered.
        // IMPORTANT: SetSubtypeAsync is unreliable here — the platform marks
        // OnAfterPost non-cancellable and swallows nested exceptions.
        // The real transition is ReleaseRealizationCommand AFTER SaveDocumentAsync.
        // The block below is only for direct API / programmatic posts (bypass).
        var sourceOrder = invoice?.SourceOrder ?? header.SourceOrder;
        if (sourceOrder != Guid.Empty)
        {
            var order = await docs.GetDocumentAsync<SalesOrder>(sourceOrder);
            if (order is not null && order.Subtype == "Confirmed")
                await context.GetService<IDocumentPostingService>()
                    .SetSubtypeAsync(SalesOrderType, sourceOrder, "Delivered");
        }

        return EventResult.Ok();
    }

    /// <summary>
    /// DEAL CONTEXT for the tax-rule engine: flat paths → values. What was
    /// sold, to whom, and for how much — the rule picks a code from this set
    /// instead of the single default from settings.
    ///
    /// A dictionary, not a typed class, on purpose: the engine is decoupled
    /// from the document (Purchasing puts its own keys) and survives the
    /// contracts assembly boundary, which cannot see script types.
    ///
    /// The path set is a CONTRACT with rule authors, so it stays narrow and
    /// grows when needed, not "just in case": a path nobody writes looks
    /// live in a rule and silently never matches. Line homogeneity is not
    /// required — an invoice may mix item groups, so item.group is set ONLY
    /// when every line shares one; otherwise the path is absent and the rule
    /// honestly misses (NotExists sees that). Per-line tax is a later job.
    /// </summary>
    private static async Task<Dictionary<string, object?>> TaxContextAsync(
        SalesRealization invoice, decimal taxBase, EventContext context)
    {
        var ctx = new Dictionary<string, object?>
        {
            ["document.type"] = "SalesRealization",
            ["direction"] = "OUTPUT",
            ["amount"] = taxBase,
        };

        var customer = await context.GetService<IDictionaryManager<Customer>>().GetRecordAsync(invoice.Customer);
        if (customer is not null)
        {
            ctx["buyer.type"] = customer.CustomerType;
            ctx["buyer.name"] = customer.Name;
            ctx["buyer.id"] = customer.MetaId.ToString("D");
        }

        if (invoice.LegalEntity != Guid.Empty)
            ctx["seller.id"] = invoice.LegalEntity.ToString("D");

        var items = context.GetService<IDictionaryManager<Item>>();
        var groups = new HashSet<Guid>();
        var lineItems = new HashSet<Guid>();
        foreach (var line in invoice.Lines)
        {
            lineItems.Add(line.Item);
            var item = await items.GetRecordAsync(line.Item);
            if (item is not null) groups.Add(item.ItemGroup);
        }
        if (lineItems.Count == 1)
            ctx["item.id"] = lineItems.First().ToString("D");
        if (groups.Count == 1)
        {
            var group = await context.GetService<IDictionaryManager<ItemGroup>>().GetRecordAsync(groups.First());
            if (group is not null) ctx["item.group"] = group.Code;
            ctx["item.groupId"] = groups.First().ToString("D");
        }

        return ctx;
    }

    // Before unpost/cancel: about to reverse the document's movements.
    public override Task<EventResult> OnBeforeUnpostAsync(SalesRealization header, EventContext context)
        => next(header, context);

    // After the document's movements were reversed.
    public override Task<EventResult> OnAfterUnpostAsync(SalesRealization header, EventContext context)
        => next(header, context);

    // Human-readable description shown in lists: put it in context.Data["description"].
    public override async Task<EventResult> OnGenerateDescriptionAsync(SalesRealization header, EventContext context){
        var prior = await next(header, context);
        if (!prior.Success) return prior;

        // context.Data["description"] = "SalesRealization " + header.Number;
        return EventResult.Ok();
    }

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(SalesRealization header, string errorMessage, EventContext context)
        => next(header, errorMessage, context);

    // A delete failed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => next(recordId, context);
}
