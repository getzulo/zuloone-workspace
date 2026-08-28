#nullable enable
using System;
using System.Linq;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// Звено ЦЕПОЧКИ обработчиков SalesInvoice из модели GLIntegration: при выставлении
// счёта продажа разносится в главную книгу (Dr дебиторка / Cr выручка).
//
// ВНИМАНИЕ: имя класса СВОЁ, не как у базового обработчика. Попытка назвать
// его именем базового (ради цепочки) не создаёт цепочку, а ВЫТЕСНЯЕТ родной
// обработчик Sales вместе с его проверкой остатка. Обратная сторона: со своим
// именем это звено пока НЕ ВЫЗЫВАЕТСЯ (см. заметку ниже).
// Исходное рассуждение про имя класса:
// расширение чужого объекта оформляется звеном цепочки (конверт несёт
// extensionMetaId + baseClassName), и рантайм связывает звенья ПО ИМЕНИ КЛАССА.
// Со своим именем класса скрипт становится КОНКУРИРУЮЩИМ обработчиком того же
// документа — и не выполняется вовсе, потому что запускается только самый
// производный. Именно так эта разноска и молчала, когда у Sales появился
// собственный обработчик с проверкой остатка.
//
// Событие тонкое: сумма, юрлицо — и вызов GeneralLedgerService, где живёт вся
// механика проводки. Разноска best-effort: не настроены счета/период — тихо мимо.
public partial class SalesGLEventHandler : TypedDocumentEventHandler<SalesInvoice>
{
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
        var gl = context.GetService<IGeneralLedgerService>();
        var settings = await gl.GetSettingsAsync();
        if (settings == null) return null;

        // Строки заголовочного события пусты — документ перечитывается целиком.
        var inv = await context.GetService<IDocumentManager>().GetDocumentAsync<SalesInvoice>(header.MetaId);
        if (inv == null) return null;

        // Сумма считается здесь, а не общим PricingService: в скомпилированном
        // обработчике событий его контракт приходит из другой версии сборки
        // ZuloOne.Services.Contracts и не кастится (в транзакционных скриптах —
        // работает). Округление то же, что у PricingService.
        var total = inv.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero));

        // Юрлицо — по цепочке Ячейка → Зона → Склад → Подразделение → Юрлицо.
        var loc = await context.GetService<IDictionaryManager<StoreCell>>().GetRecordAsync(inv.Location);
        if (loc == null) return null;
        var zone = await context.GetService<IDictionaryManager<StoreZone>>().GetRecordAsync(loc.StoreZone);
        if (zone == null) return null;
        var wh = await context.GetService<IDictionaryManager<Store>>().GetRecordAsync(zone.Store);
        if (wh == null) return null;
        var div = await context.GetService<IDictionaryManager<Division>>().GetRecordAsync(wh.Division);
        if (div == null) return null;
        var le = await context.GetService<IDictionaryManager<LegalEntity>>().GetRecordAsync(div.LegalEntity);
        if (le == null) return null;

        return await gl.PostAsync(
            DateTime.UtcNow.Date, le.MetaId, le.Currency, total,
            settings.ArAccountCode, settings.RevenueAccountCode,
            "Sales invoice " + header.MetaId,
            "Дебиторка по продаже", "Выручка от продажи");
    }
}
