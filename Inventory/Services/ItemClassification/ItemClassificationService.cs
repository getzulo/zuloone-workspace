using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Core.Services;
using ZuloOne.Managers;
using ZuloOne.Runtime.Generated;

// Что ОБЩЕГО у набора товаров.
//
// ЗАЧЕМ ЭТО СУЩЕСТВУЕТ. Код налога можно пинить на товарную группу
// (`TaxMapping`, SourceType = ItemGroup), но налоговый расчёт создаётся ОДИН на
// документ с одной общей базой, а не по строке. Значит подставить группу в
// контекст определения честно можно только тогда, когда она у ВСЕХ строк одна.
// Иначе весь документ облагался бы по группе случайно выбранной строки — и
// зависело бы это от порядка строк, то есть было бы недетерминированно.
//
// ПОЧЕМУ ЗДЕСЬ, А НЕ В Tax. Группа лежит на `Item`, а Tax — слой 1 и на
// Inventory не ссылается (и не должен: налог обязан работать на стенде без
// склада). Зато и Sales, и Purchasing от Inventory зависят оба, а дублировать
// этот разбор в пяти обработчиках — ровно тот копипаст, из-за которого правила
// расходятся.
//
// ОБЩИЙ ТОВАР ЗДЕСЬ НЕ СЧИТАЕТСЯ НАРОЧНО: это чистая арифметика по строкам
// (`lines.Select(l => l.Item).Distinct()`), справочник для неё не нужен, и
// вызывающий делает её сам одной строкой.
public partial class ItemClassification
{
    private readonly IDictionaryManager<Item> _items;

    public ItemClassification(IDictionaryManager<Item> items) => _items = items;

    /// <summary>
    /// Товарная группа, общая для всех переданных товаров, или `Guid.Empty`,
    /// если групп больше одной, список пуст или хоть у одного товара группа не
    /// заполнена.
    ///
    /// `Guid.Empty` — НЕ ошибка, а «подставлять нечего»: вызывающий просто не
    /// кладёт ключ в контекст, и определение уходит на сторону сделки.
    /// </summary>
    public async Task<Guid> CommonGroupAsync(List<Guid> itemIds)
    {
        if (itemIds is null || itemIds.Count == 0) return Guid.Empty;

        var distinct = itemIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0) return Guid.Empty;

        var group = Guid.Empty;
        foreach (var id in distinct)
        {
            var item = await _items.GetRecordAsync(id);
            var own = item?.ItemGroup ?? Guid.Empty;

            // Товар без группы рушит однородность: сказать «группа у всех одна»
            // про набор, часть которого вне групп, нельзя.
            if (own == Guid.Empty) return Guid.Empty;

            if (group == Guid.Empty) group = own;
            else if (group != own) return Guid.Empty;
        }

        return group;
    }
}
