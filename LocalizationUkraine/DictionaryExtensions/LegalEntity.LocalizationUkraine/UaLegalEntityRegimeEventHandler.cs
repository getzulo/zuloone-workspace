#nullable enable
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// ═══ СОПОСТАВЛЕНИЕ ОСВОБОЖДЕНИЯ ЕДЕТ СО СМЕНОЙ РЕЖИМА ═══════════════════════
//
// Регистры пакета ветвятся по UaTaxRegime сами. Общий путь Sales → Tax — нет:
// он берёт DefaultTaxCode, и у спрощенця 5% в леджере оказался бы ПДВ, которого
// нет в UaVatPayable. Штатное лечение — TaxMapping на юрлицо. Раньше его
// заводили руками; теперь смена режима на карточке делает это сама.
//
// ЗОВЁМ ПОСЛЕ next: сохранение юрлица должно пройти, даже если сопоставление
// не завелось (код в настройках пуст — это то же молчание, что у ЄП без кода).
[ExtensionOf("LegalEntity")]
public partial class UaLegalEntityRegimeEventHandler : TypedDictionaryEventHandler<LegalEntity>
{
    public override async Task<EventResult> OnAfterSaveAsync(
        LegalEntity record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;

        await context.GetService<IUaFirstEvent>().SyncExemptMappingAsync(record.MetaId);
        return EventResult.Ok();
    }
}
