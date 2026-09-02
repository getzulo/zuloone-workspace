#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Strongly-typed lifecycle handler for SalesInvoice documents.
// `header` is a typed SalesInvoice entity — access fields directly (header.Number).
// Record events (insert/update/delete/validate) plus the document-only posting
// events are stubbed below. Cancel a transition with EventResult.Cancel("reason");
// document table-part rows are available via document.TableParts["Name"].
public partial class SalesInvoiceEventHandler : TypedDocumentEventHandler<SalesInvoice>
{
    // Building a new document server-side: seed header defaults (number, date).
    public override Task<EventResult> OnBeforeCreateAsync(SalesInvoice header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // MIQS BeforeSave: runs before ANY save — insert (isNew) or update.
    // DiscountPercent вне [0, 100] переворачивает знак LineAmount (>100%) или значит
    // не скидку, а наценку (<0) — оба случая ловятся здесь, а не только у источника
    // (LoyaltyTier), потому что поле пишется и напрямую рукой на форме документа.
    // На точечном обновлении подтипа (SetSubtypeAsync) header — частичный экземпляр,
    // и DiscountPercent в нём ноль (см. SalesInvoiceLoyaltyDiscountHandler) — в
    // диапазоне, проверка безвредна.
    public override Task<EventResult> OnBeforeSaveAsync(SalesInvoice header, bool isNew, EventContext context)
    {
        if (header.DiscountPercent < 0m || header.DiscountPercent > 100m)
            return Task.FromResult(EventResult.Cancel("Скидка на счёте должна быть в диапазоне от 0 до 100%"));
        return Task.FromResult(EventResult.Ok());
    }

    // MIQS AfterSave: runs after ANY save (insert or update).
    public override Task<EventResult> OnAfterSaveAsync(SalesInvoice header, bool isNew, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Operation-specific hooks. NOTE: overriding one REPLACES OnBeforeSave/OnAfterSave
    // for that operation (the default implementation is what delegates to them).
    //public override Task<EventResult> OnBeforeInsertAsync(SalesInvoice header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterInsertAsync(SalesInvoice header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnBeforeUpdateAsync(SalesInvoice header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());
    //public override Task<EventResult> OnAfterUpdateAsync(SalesInvoice header, EventContext context)
    //    => Task.FromResult(EventResult.Ok());

    // Just before the document is deleted.
    public override Task<EventResult> OnBeforeDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the document was deleted.
    public override Task<EventResult> OnAfterDeleteAsync(Guid recordId, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Before posting: reject overselling — a line cannot ship more than is on hand
    // at the sale location. Stock is a single-entry register (allowNegativeBalance:true),
    // so the engine does not guard this; the check lives here (reads on-hand via
    // IRegisterMovementService.GetBalanceAsync on the physical Item+Cell dimensions).
    // Note: check-then-act, not atomic with posting.
    private static readonly Guid StockRegister = Guid.Parse("83559331-ac7f-46da-87a8-7da599ef6f41");

    /// <summary>Тип документа — цель точечного обновления шапки.</summary>
    private static readonly Guid SalesInvoiceType = Guid.Parse("34a1af4c-aeaf-48d1-8626-9a0a13b2d5c3");

    public override async Task<EventResult> OnBeforePostAsync(SalesInvoice header, EventContext context)
    {
        // ПОДТИП «ОПЛАЧЕН» НЕДОСТИЖИМ НАМЕРЕННО, И ЭТО НАДО ЗАЩИЩАТЬ ЯВНО.
        //
        // Подтип объявлен (исторические документы и отчёты), но ребра Issued→Paid
        // в карте переходов больше нет — форма его не предлагает. Замок здесь
        // на случай прямого API. К Issued привязаны ТРИ транзакционных скрипта:
        // дебиторка (Sales), баллы лояльности (CRM) и страновой НДС
        // (LocalizationSaudiArabia). Переход снял бы движения покидаемого
        // состояния — долг БЕЗ оплаты, баллы и обязательство по налогу. Paid
        // помечен isReadOnly, выйти из него было бы нельзя.
        //
        // Оплата в этой системе — ОТДЕЛЬНЫЙ документ (CustomerPayment), гасящий
        // регистр Receivable и не трогающий счёт. Платёжный статус читается из
        // регистра, а не из подтипа. Тот же урок записан в отключённой команде
        // MarkPaid; здесь он закрыт на замок, а не только объяснён комментарием.
        if (header.Subtype == "Paid")
            return EventResult.Cancel(
                "Счёт нельзя перевести в «Оплачен» вручную: это снимет дебиторку без оплаты, "
                + "баллы лояльности и начисленный НДС. Проведите оплату документом CustomerPayment — "
                + "он погасит долг, а счёт останется выставленным.");

        if (header.Subtype != "Issued") return EventResult.Ok();

        var full = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesInvoice>(header.MetaId);
        var lines = full?.Lines ?? header.Lines;

        // Адресная дисциплина: отгружать положено из ячейки ОТБОРА, куда товар
        // принесло задание отбора. Проверка спрашивает Inventory, а не сравнивает
        // имя типа ячейки. Дисциплина выключена (умолчание) — годится любая
        // ячейка, и счёт выставляется как раньше.
        if (!await context.GetService<IStoreCellService>()
                .IsCellAllowedForAsync(full?.Location ?? header.Location, StoreCellPurpose.Picking))
            return EventResult.Cancel(
                "Отгрузка идёт из ячейки ОТБОРА — у выбранной ячейки другое назначение");

        // ЮРЛИЦО ПРОДАВЦА ФИКСИРУЕТСЯ НА ДОКУМЕНТЕ, а не резолвится каждым, кому
        // оно понадобилось. Причина техническая и жёсткая: налоговый леджер разрезан
        // юрлицом, а пишет в него ТРАНЗАКЦИОННЫЙ скрипт — синхронный, и цепочку
        // Ячейка → Зона → Склад → Подразделение → Юрлицо он пройти не может (это
        // четыре асинхронных чтения справочников). Здесь, на пути проведения, они
        // работают штатно.
        //
        // Причина учётная не слабее: оргструктуру потом переподчинят, а счёт
        // неизменен — он обязан помнить, КТО продал, а не пересчитывать это по
        // сегодняшнему дереву. Поэтому заполненное вручную значение не
        // перезаписывается: ячейка отгрузки и продавец совпадают не всегда.
        //
        // Пишется ТОЧЕЧНЫМ обновлением шапки, а не присваиванием в header:
        // экземпляр, приехавший в событие, до базы не доезжает (проверено —
        // IssueStampsSellingLegalEntity падал именно на этом). SaveDocumentAsync
        // здесь тоже не годится: он переписывает ВСЕ строки документа посреди его
        // же проведения.
        var current = full?.LegalEntity ?? header.LegalEntity;
        if (current == Guid.Empty)
        {
            var resolved = await context.GetService<IStoreCellService>()
                .GetLegalEntityAsync(full?.Location ?? header.Location);
            if (resolved.HasValue)
            {
                header.LegalEntity = resolved.Value;
                await context.GetService<IDocumentManager>().UpdateDocumentAsync(
                    SalesInvoiceType, header.MetaId,
                    new Dictionary<string, object?> { ["LegalEntity"] = resolved.Value });
            }
        }

        // Сравнивается с остатком регистра, а он в БАЗОВОЙ единице товара — значит и
        // спрос считается по BaseQuantity. Ноль = единица не указана, пересчёта не было.
        // Налоговая база ниже (OnAfterPostAsync) НАРОЧНО остаётся на введённом
        // Quantity: цена задана за введённую единицу, продано 5 ящиков по цене за ящик.
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

        // Налоговый контур НАСТРОЕН, но на дату счёта действующей ставки нет —
        // счёт не выставляется. Это не «налоги выключены» (тогда кода по умолчанию
        // просто нет и проверка молчит), а порванная настройка: счёт, молча ушедший
        // клиенту без НДС, обнаружится у налогового органа.
        //
        // Проверка стоит ЗДЕСЬ, в отменяемом событии, а не рядом с порождением
        // расчёта в OnAfterPost: OnAfterPost платформа объявляет неотменяемым и
        // превращает исключение обработчика в предупреждение в логе — документ всё
        // равно проводится, и потеря налога снова становится молчаливой.
        var tax = context.GetService<ITaxService>();
        var taxCode = await tax.ResolveDefaultTaxCodeAsync();
        if (taxCode is null) return EventResult.Ok();

        var taxPoint = TaxPointOf(header);
        var rate = await tax.ResolveRateAsync(taxCode.Value, taxPoint);
        if (rate is null)
            return EventResult.Cancel(
                $"Налоговый код настроен, но действующей ставки на {taxPoint:yyyy-MM-dd} нет — счёт не выставляется");

        // СТАВКА ФИКСИРУЕТСЯ НА ДОКУМЕНТЕ — по той же причине, что юрлицо выше:
        // страновые проводки (НДС КСА в регистр VatPayable) синхронны и подобрать
        // датированную ставку сами не могут. Раньше локализация брала её из плоской
        // константы SaudiVatRate, у которой нет даты вовсе: при любом изменении
        // ставки страновой регистр расходился с TaxLedger, а счёт задним числом
        // считался по сегодняшней ставке. Теперь источник один — справочник TaxRate,
        // подобранный на дату счёта ровно тем же вызовом, которым воспользуется
        // расчёт налога в OnAfterPost.
        //
        // Точечным обновлением, а не присваиванием: экземпляр из события до базы
        // не доезжает (см. примечание к LegalEntity). Заполненное вручную не
        // перезаписывается — счёт мог быть выставлен по согласованной ставке.
        if (header.TaxRateApplied == 0m && (full?.TaxRateApplied ?? 0m) == 0m)
        {
            header.TaxRateApplied = rate.Value;
            await context.GetService<IDocumentManager>().UpdateDocumentAsync(
                SalesInvoiceType, header.MetaId,
                new Dictionary<string, object?> { ["TaxRateApplied"] = rate.Value });
        }

        return EventResult.Ok();
    }

    /// <summary>Дата налогового события — дата документа; незаполненная датируется
    /// сегодняшним днём ровно так же, как её проставляет IDocumentManager при создании.</summary>
    private static DateTime TaxPointOf(SalesInvoice header)
        => header.DocumentDate == default ? DateTime.UtcNow.Date : header.DocumentDate.Date;

    // Выставленный счёт порождает расчёт ВЫХОДНОГО налога: отдельный документ
    // TaxCalculation, связанный со счётом через граф документов. Отдельный
    // документ, а не поле на счёте, потому что налог живёт своей жизнью — у него
    // свой леджер, своя отчётность и своя дата налогового события.
    //
    // Порождение здесь, а не в проводке: ставка и код налога читаются из
    // справочников асинхронно, а GetTransactions синхронный.
    public override async Task<EventResult> OnAfterPostAsync(SalesInvoice header, EventContext context)
    {
        if (header.Subtype != "Issued") return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var invoice = await docs.GetDocumentAsync<SalesInvoice>(header.MetaId);
        if (invoice is null || invoice.Lines.Count == 0) return EventResult.Ok();

        // Юрлицо продавца УЖЕ зафиксировано на документе (OnBeforePost) — читается
        // оттуда, а не резолвится заново по ячейке. Так налог и сам счёт по
        // построению говорят об одном и том же продавце, даже если оргструктуру
        // переподчинят между проведением и перепроведением.
        var pricing = context.GetService<IPricingService>();
        var legalEntity = invoice.LegalEntity;
        if (legalEntity != Guid.Empty)
        {
            var taxBase = invoice.Lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice, invoice.DiscountPercent));

            // Контур необязателен: не настроен — сервис вернёт null, счёт выставлен
            // как раньше. Ставка подбирается на ДАТУ СЧЁТА, не на сегодня: иначе
            // счёт и его налог датировались бы по-разному, а задним числом
            // выставленный документ посчитался бы по сегодняшней ставке.
            var calc = await context.GetService<ITaxService>()
                .CreateCalculationAsync(legalEntity, "OUTPUT", taxBase, $"Sales invoice {header.Number}",
                    TaxPointOf(header), await TaxContextAsync(invoice, taxBase, context));
            if (calc.HasValue)
                await docs.AddLinkAsync(header.MetaId, calc.Value);
        }

        // Захват цены в историю — самостоятельная забота: срабатывает даже без
        // юрлица. Одна и та же (Item,Unit) на двух строках — выигрывает последняя.
        foreach (var line in invoice.Lines)
            await pricing.CaptureSalePriceAsync(line.Item, line.Unit, invoice.Customer, line.UnitPrice, TaxPointOf(header));

        return EventResult.Ok();
    }

    /// <summary>
    /// КОНТЕКСТ СДЕЛКИ для движка правил налога: плоские пути → значения. Что
    /// именно продали, кому и на сколько — по этому набору правило и выбирает код,
    /// вместо единственного кода по умолчанию из настроек.
    ///
    /// Словарь, а не типизированный класс, — сознательно: движок развязан с
    /// документом (Purchasing кладёт сюда своё) и переживает границу сборки
    /// контрактов, которая типов из скриптов не видит.
    ///
    /// Набор путей — ДОГОВОР с теми, кто заводит правила, поэтому он узкий и
    /// расширяется по мере надобности, а не «на всякий случай»: путь, которого
    /// никто не кладёт, в правиле выглядит рабочим, а молча не срабатывает.
    /// Однородность строки не требуется — в счёте могут быть товары разных групп,
    /// поэтому item.group кладётся ТОЛЬКО когда он у всех строк один; иначе пути
    /// нет вовсе, и правило по нему честно не сработает (оператор NotExists это
    /// увидит). Налог по строкам — задача построчного расчёта, он отложен.
    /// </summary>
    private static async Task<Dictionary<string, object?>> TaxContextAsync(
        SalesInvoice invoice, decimal taxBase, EventContext context)
    {
        var ctx = new Dictionary<string, object?>
        {
            ["document.type"] = "SalesInvoice",
            ["direction"] = "OUTPUT",
            ["amount"] = taxBase,
        };

        var customer = await context.GetService<IDictionaryManager<Customer>>().GetRecordAsync(invoice.Customer);
        if (customer is not null)
        {
            ctx["buyer.type"] = customer.CustomerType;
            ctx["buyer.name"] = customer.Name;
        }

        var items = context.GetService<IDictionaryManager<Item>>();
        var groups = new HashSet<Guid>();
        foreach (var line in invoice.Lines)
        {
            var item = await items.GetRecordAsync(line.Item);
            if (item is not null) groups.Add(item.ItemGroup);
        }
        if (groups.Count == 1)
        {
            var group = await context.GetService<IDictionaryManager<ItemGroup>>().GetRecordAsync(groups.First());
            if (group is not null) ctx["item.group"] = group.Code;
        }

        return ctx;
    }

    // Before unpost/cancel: about to reverse the document's movements.
    public override Task<EventResult> OnBeforeUnpostAsync(SalesInvoice header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // After the document's movements were reversed.
    public override Task<EventResult> OnAfterUnpostAsync(SalesInvoice header, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // Human-readable description shown in lists: put it in context.Data["description"].
    public override Task<EventResult> OnGenerateDescriptionAsync(SalesInvoice header, EventContext context)
    {
        // context.Data["description"] = "SalesInvoice " + header.Number;
        return Task.FromResult(EventResult.Ok());
    }

    // An insert/update failed: return Error("friendly text") to replace the raw DB error.
    public override Task<EventResult> OnSaveFailedAsync(SalesInvoice header, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());

    // A delete failed.
    public override Task<EventResult> OnDeleteFailedAsync(Guid recordId, string errorMessage, EventContext context)
        => Task.FromResult(EventResult.Ok());
}
