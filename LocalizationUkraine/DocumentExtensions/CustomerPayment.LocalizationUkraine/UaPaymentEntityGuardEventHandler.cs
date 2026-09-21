#nullable enable
using System;
using System.Collections.Generic;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

// ═══ ЮРЛИЦО ОПЛАТЫ ОБЯЗАНО СОВПАСТЬ С ЮРЛИЦОМ ДОГОВОРА ═══════════════════════
//
// ЗАЧЕМ ЭТО ВООБЩЕ. UaVatFirstEvent разрезан юрлицом, потому что режим
// налогообложения — свойство юрлица: в одной системе живут ТОВ и несколько ФОП.
// Отгрузка кладёт Shipped в координату продавца, оплата кладёт Paid в координату
// из ШАПКИ. И если они разойдутся, первое событие развалится тихо: max(Shipped,
// Paid) посчитается в каждой координате ОТДЕЛЬНО, каждая стартует с нуля, и налог
// начислится ДВАЖДЫ — один раз по отгрузке, второй раз по оплате той же поставки.
// Ни ошибки, ни предупреждения: обе цифры по отдельности выглядят правильными.
//
// ПОЧЕМУ ИМЕННО ОПЛАТА. Счёту продавца проставляет обработчик — из договора, а
// если там пусто, по ячейке отгрузки. Оплате не проставляет НИКТО: в шапке стоит
// то, что выбрал оператор из полного списка юрлиц, и до этой проверки ничто не
// мешало выбрать соседний ФОП.
//
// ПУСТОЕ ЗНАЧЕНИЕ НЕ ОТКЛОНЯЕТСЯ, А ДОСТАВЛЯЕТСЯ ИЗ ДОГОВОРА. Отклонять было бы
// проще, но неправильно вдвойне: оплаты без юрлица создают и живые сценарии, и
// чужие тесты (Sales их заводит именно так), а «пусто» — это не спор с договором,
// это отсутствие данных, которые договор как раз и знает. Молча же оставить пусто
// нельзя: Shipped ушёл бы в координату продавца, Paid — в пустую, и получилось бы
// то самое двойное начисление.
//
// ШТАМП ИДЁТ ЧЕРЕЗ UpdateDocumentAsync, А НЕ ПРИСВАИВАНИЕМ. Экземпляр события до
// базы не доезжает — на этом уже спотыкался обработчик счёта, — поэтому пишем
// адресно шапку и тут же правим экземпляр, чтобы транзакционный скрипт, который
// читает снимок ЭТОГО же проведения, увидел проставленное значение.
// SaveDocumentAsync здесь нельзя: он переписывает ВСЕ строки посреди проведения.
[ExtensionOf("CustomerPayment")]
public partial class UaPaymentEntityGuardEventHandler : TypedDocumentEventHandler<CustomerPayment>
{
    public override async Task<EventResult> OnBeforePostAsync(CustomerPayment document, EventContext context)
    {
        var prior = await next(document, context);
        if (!prior.Success) return prior;

        var documents = context.GetService<IDocumentManager>();
        // Событие несёт только те колонки, что пишутся, — строк в нём может не
        // быть вовсе. Договоры живут в строках, поэтому документ перечитываем.
        var payment = await documents.GetDocumentAsync<CustomerPayment>(document.MetaId) ?? document;
        if (payment.Lines is null || payment.Lines.Count == 0) return EventResult.Ok();

        var firstEvent = context.GetService<IUaFirstEvent>();
        var header = payment.LegalEntity;
        var fromContracts = Guid.Empty;

        foreach (var line in payment.Lines)
        {
            if (line.Contract == Guid.Empty) continue;

            var owner = await firstEvent.LegalEntityOfAsync(line.Contract);
            if (owner == Guid.Empty) continue; // договор юрлица не знает — сверять не с чем

            if (header != Guid.Empty)
            {
                if (owner != header)
                    return EventResult.Cancel(
                        "Юрлицо в шапке оплаты не совпадает с юрлицом договора в строке. " +
                        "Первое событие по ПДВ копится ПО ЮРЛИЦУ: разойдись они, отгрузка и " +
                        "оплата по одной поставке попали бы в разные накопления и налог был бы " +
                        "начислен дважды. Выберите юрлицо договора или разнесите строки по " +
                        "отдельным оплатам.");
                continue;
            }

            if (fromContracts == Guid.Empty) fromContracts = owner;
            else if (fromContracts != owner)
                return EventResult.Cancel(
                    "В одной оплате строки по договорам РАЗНЫХ юрлиц, а юрлицо в шапке не " +
                    "заполнено — подставить нечего. Укажите юрлицо в шапке или разнесите " +
                    "строки по отдельным оплатам.");
        }

        if (header == Guid.Empty && fromContracts != Guid.Empty)
        {
            document.LegalEntity = fromContracts;
            // Тип берём с ПЕРЕЧИТАННОГО документа: экземпляр события несёт только
            // записываемые колонки, и DocumentType в нём пустой — адресное
            // обновление на Guid.Empty падает «Document type … not found».
            await documents.UpdateDocumentAsync(
                payment.DocumentType, document.MetaId,
                new Dictionary<string, object?> { ["LegalEntity"] = fromContracts });
        }

        return EventResult.Ok();
    }
}
