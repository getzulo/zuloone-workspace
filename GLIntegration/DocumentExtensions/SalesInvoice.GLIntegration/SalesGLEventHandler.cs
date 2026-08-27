#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Расширение Sales: при выставлении счёта продажи разносятся в главную книгу
// (Dr дебиторка / Cr выручка). Логика инлайнится в событие намеренно — вынести
// её в сервис нельзя: он тянет SalesInvoice + Accounting + Organization +
// Inventory (это только верхний модуль GLIntegration), а скрипт модуля не может
// ссылаться на контракт I<Service> собственного модуля.
//
// Чтение справочников — через ТИПИЗИРОВАННЫЕ IDictionaryManager<T> (loc.Warehouse,
// div.LegalEntity, acc.MetaId, period.FromDate), а не сырой IDataService с ручными
// кастами. Сырой IDataService остаётся ровно для одного: СОЗДАНИЯ документа-
// проводки — типизированного менеджера создания документа у платформы нет.
// Разноска best-effort: не настроен GL или что-то не резолвится → тихий пропуск.
public partial class SalesGLEventHandler : TypedDocumentEventHandler<SalesInvoice>
{
    private static readonly Guid JournalEntryType = Guid.Parse("188246b3-5ed0-4da0-98cb-a86b6da36581");

    public override async Task<EventResult> OnAfterPostAsync(SalesInvoice document, EventContext context)
    {
        if (document.Subtype != "Issued") return EventResult.Ok();

        try
        {
            var jeId = await PostToLedgerAsync(document, context);
            if (jeId.HasValue)
                await context.GetService<IDocumentManager>().AddLinkAsync(document.MetaId, jeId.Value);
        }
        catch
        {
            // Разноска GL зависит от настройки и не должна ронять проведение счёта.
        }

        return EventResult.Ok();
    }

    private async Task<Guid?> PostToLedgerAsync(SalesInvoice header, EventContext context)
    {
        var arCode = GlobalConstants.Get<string>("ArAccountCode");
        var revCode = GlobalConstants.Get<string>("RevenueAccountCode");
        if (string.IsNullOrWhiteSpace(arCode) || string.IsNullOrWhiteSpace(revCode)) return null;

        // Документ и сумма — через IDocumentManager (строки заголовочного события пусты).
        var docs = context.GetService<IDocumentManager>();
        var inv = await docs.GetDocumentAsync<SalesInvoice>(header.MetaId);
        if (inv == null) return null;
        var total = inv.Lines.Sum(l => l.Quantity * l.UnitPrice);
        if (total <= 0m) return null;

        // Счета разноски — типизированный менеджер плана счетов.
        var accounts = context.GetService<IDictionaryManager<ChartOfAccounts>>();
        var arAcc = (await accounts.GetRecordsAsync($"Code = '{arCode}'")).FirstOrDefault();
        var revAcc = (await accounts.GetRecordsAsync($"Code = '{revCode}'")).FirstOrDefault();
        if (arAcc == null || revAcc == null) return null;

        // Юрлицо и валюта по цепочке Location → Warehouse → Division → LegalEntity.
        var loc = await context.GetService<IDictionaryManager<WarehouseLocation>>().GetRecordAsync(inv.Location);
        if (loc == null) return null;
        var wh = await context.GetService<IDictionaryManager<Warehouse>>().GetRecordAsync(loc.Warehouse);
        if (wh == null) return null;
        var div = await context.GetService<IDictionaryManager<Division>>().GetRecordAsync(wh.Division);
        if (div == null) return null;
        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(div.LegalEntity);
        if (le == null) return null;

        // Учётный период, покрывающий дату разноски.
        var date = DateTime.UtcNow.Date;
        var period = (await context.GetService<IDictionaryManager<FiscalPeriod>>().GetRecordsAsync())
            .FirstOrDefault(p => date >= p.FromDate.Date && date <= p.ToDate.Date);
        if (period == null) return null;

        // СОЗДАНИЕ документа-проводки: типизированного менеджера создания документа
        // у платформы нет — заголовок и строки вставляются через IDataService, затем
        // перевод в «Проведено» исполняет GLPostingTx → движения по GL.
        var data = context.GetService<IDataService>();
        var jeId = await data.InsertAsync("JournalEntry", new Dictionary<string, object?>
        {
            ["Subtype"] = "Draft",
            ["StatusValue"] = null,
            ["DocumentDate"] = date,
            ["LegalEntity"] = le.MetaId,
            ["FiscalPeriod"] = period.MetaId,
            ["Currency"] = le.Currency,
            ["Description"] = "Sales invoice " + header.MetaId
        });

        await data.InsertAsync("TP_JournalEntryLines", new Dictionary<string, object?>
        {
            ["OwnerMetaId"] = jeId, ["Account"] = arAcc.MetaId, ["Debit"] = total, ["Credit"] = 0m, ["Description"] = "Дебиторка по продаже"
        });
        await data.InsertAsync("TP_JournalEntryLines", new Dictionary<string, object?>
        {
            ["OwnerMetaId"] = jeId, ["Account"] = revAcc.MetaId, ["Debit"] = 0m, ["Credit"] = total, ["Description"] = "Выручка от продажи"
        });

        await context.GetService<IDocumentPostingService>().SetSubtypeAsync(JournalEntryType, jeId, "Posted");
        return jeId;
    }
}
