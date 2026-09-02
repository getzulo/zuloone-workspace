---
name: zuloone-extend
description: Расширить ЧУЖУЮ модель ZuloOne — поля-расширения на чужих объектах, звено цепочки событий, наследование кода скриптов, прививка меню. Use when adding fields, event handlers, script overrides or menu items to objects owned by another model.
---

# Расширение чужой модели

Чужие модели не редактируются — они расширяются АДДИТИВНО из твоей модели.
Четыре механики: поля-расширения, звено цепочки событий, наследование кода,
прививка меню.

## 0. Предусловия — без них расширение отклонят

1. **Зависимость объявлена**: ребро на модель-владельца в твоём `model.json`
   (скилл `zuloone-new-model`). Ссылка вне транзитивного замыкания
   зависимостей — нарушение depends-гейта.
2. **Слой строго выше** слоя расширяемой модели.
3. `Core/` расширяется как любая модель, но НЕ редактируется; `isSealed`-модель
   — тоже только расширяется.

## 1. Поля на чужом объекте — экстеншен-агрегат

Раскладка §8.2: тип-папки зеркально базовым, имя агрегата = `<Цель>.<ТвояМодель>`
(глобально уникально, один агрегат на пару цель+модель):

`DictionaryExtensions/Country.WMS/Country.WMS.extension.json`:
```json
{
  "kind": "DictionaryExtension",
  "object": {
    "targetDictionaryMetaId": "<GUID чужого справочника>",
    "description": "<Зачем расширяем>",
    "metaId": "<GUID-агрегата>", "name": "Country.WMS",
    "modelId": "<GUID ТВОЕЙ модели>", "layerId": 2
  },
  "fields": [
    { "dictionaryMetaId": "<GUID чужого справочника>", "extensionMetaId": "<GUID-агрегата>",
      "fieldName": "CustomsCode", "name": "CustomsCode", "caption": "Код таможни",
      "baseType": "String", "length": 32, "displayOrder": 50, "isVisible": true,
      "metaId": "<GUID>", "modelId": "<GUID ТВОЕЙ модели>", "layerId": 2 }
  ]
}
```

Для документов — `DocumentExtensions/<Цель>.<Модель>/` тем же лекалом
(`targetDocumentTypeMetaId`). Поле живёт в ТВОЕЙ модели (row-слоение): отключение
твоей модели убирает поле из effective set. В сгенерированном классе сущности
поле появляется как обычное свойство — доступно всем скриптам.

## 2. Звено цепочки событий на чужом объекте

Обработчики одного объекта выстраиваются в ЦЕПОЧКУ по слоям: владелец первым,
расширения после. Звено — обычный EventHandler-скрипт твоей модели в папке
агрегата:

`DictionaryExtensions/Country.WMS/CountryWmsEvents.script.json`:
```json
{
  "kind": "Script",
  "object": {
    "scriptType": "EventHandler", "objectType": "Dictionary",
    "objectMetaId": "<GUID чужого справочника>", "objectName": "Country",
    "extensionMetaId": "<GUID-агрегата>",
    "metaId": "<GUID>", "name": "CountryWmsEvents",
    "modelId": "<GUID ТВОЕЙ модели>", "layerId": 2
  }
}
```

`CountryWmsEvents.cs` — класс с **УНИКАЛЬНЫМ именем** (не `CountryEventHandler` —
так уже зовётся звено владельца):

```csharp
#nullable enable
namespace ZuloOne.Runtime.Generated;

public partial class CountryWmsEventHandler : TypedDictionaryEventHandler<Country>
{
    public override Task<EventResult> OnBeforeSaveAsync(Country record, bool isNew, EventContext context)
    {
        // выполняется ПОСЛЕ звена владельца: record несёт всё, что оно
        // записало; context.PreviousResult — результат предыдущего звена
        // (Data объединяется по всей цепочке — позднее звено дополняет,
        // но не затирает). Свои поля-расширения типизированы: record.CustomsCode.
        return Task.FromResult(EventResult.Ok());
    }
}
```

Правила цепочки: порядок = слой модели звена (затем имя); фейл любого звена
(`EventResult.Cancel/Error`) прерывает цепочку; отключение модели снимает её
звено. `super()` не нужен — звено не оборачивает, а ДОПОЛНЯЕТ.

## 3. Наследование КОДА (tx-скрипты, команды) — вместо Chain of Command

Расширение исполняемого скрипта = C#-класс, наследующий класс базового скрипта;
`base.Метод(...)` — это super(). Рантайм исполняет САМЫЙ ПРОИЗВОДНЫЙ класс
цепочки; несколько расширений выстраиваются линейно по слоям.

Envelope — обычный Script твоей модели + `baseScriptMetaId`:
```json
{
  "kind": "Script",
  "object": {
    "scriptType": "TransactionScript", "objectType": "Document",
    "objectMetaId": "<GUID подтипа — тот же, что у базы>", "objectName": "<Документ>",
    "baseScriptMetaId": "<GUID базового скрипта>",
    "metaId": "<GUID>", "name": "ReceiptTx_WMS",
    "modelId": "<GUID ТВОЕЙ модели>", "layerId": 2
  }
}
```

Код объявляет базу САМ (framework-часть не генерится — она у корня цепочки).
`base.Метод(...)` — вызов родителя, его РЕЗУЛЬТАТ у тебя в руках:

```csharp
public class ReceiptTx_WMS : ReceiptTx
{
    protected override void GetTransactions(<Документ> document, TransactionPairCollection pairs, TransactionCollection transactions)
    {
        base.GetTransactions(document, pairs, transactions);
        // ← в коллекциях уже ВСЁ, что насеял родитель: можно дополнить,
        //   поправить или отфильтровать его движения перед проведением.
        // transactions.Add(new RegisterMovementSpec("CostRegister")…);
    }
}
```

Для методов с возвращаемым значением (хуки драйверов, команды) — как в
обычном C#: `var result = base.CalculatePartialAmount(…);` — получил расчёт
родителя, поправил, вернул. Аргументы можно править ДО вызова base,
результат — ПОСЛЕ; вызов base можно и опустить (полное замещение — validate
предупредит, но не заблокирует).

- базу можно НЕ вызывать — полное замещение легально (validate предупредит);
- точки расширения = `protected virtual` базового класса; `private` недоступно,
  `sealed` — запрещено переопределять;
- скаффолд всех virtual-точек c готовыми `base.()`: кнопка «Расширить» в студии
  или `POST /api/metadata/extensions/extend-script`.

## 4. Меню и прочее

- Пункт в ЧУЖУЮ группу меню: свой пункт в СВОЁМ `Menu/menu.json` с
  `parentMetaId` чужой группы (тоже зависимость!).
- Реестр всех экстеншенов стенда: страница `/extensions` и
  `GET /api/metadata/extensions`.

## 4а. Регистры и константы ПО ИМЕНИ — дыра в изоляции моделей

`RegisterMovementSpec("X")`, `PostMovementAsync("X")`, `GetBalanceAsync("X")`,
`GlobalConstants.Get<T>("Y")` адресуются СТРОКОЙ. Проверка зависимостей между
моделями работает по ТИПАМ — значит через эти вызовы можно писать в чужой
регистр модели, от которой ты не зависишь, и компилятор промолчит.

Так и накопилось: страновой НДС писался в саудовский регистр из модели `Sales`,
начисление баллов — в регистр `CRM` оттуда же (при том что `CRM` зависит от
`Sales`, а не наоборот), проводки себестоимости числились за `Purchasing`.
Каждый раз слой был продавлен ровно там, где платформа не смотрит. Хуже всего,
что у скрипта баллов в комментарии было написано «скрипт живёт в CRM» — намерение
знали, метаданные говорили обратное, и никто не замечал.

**Правило: скрипт принадлежит той модели, чьими объектами он распоряжается.**
Пишешь в чужой регистр — либо переезжай в модель-владельца расширением чужого
документа (§2 и §3), либо ОБЪЯВИ зависимость, если она честная и не даёт цикла.

Перенос делается БЕЗ пересоздания: тот же `metaId`, новые `modelId`/`layerId` и
`extensionMetaId` — база отвечает `updated: 1`, объект меняет владельца. Целевой
документ при этом не трогается: транзакционный скрипт цепляется к подтипу сам
через `objectMetaId`, в `subtypeTransactionScripts` документа его нет.

Аудит всего воркспейса — сверить имена из этих вызовов с замыканием объявленных
зависимостей модели скрипта: пройти по всем парам `*.script.json` + `.cs`,
собрать владельцев регистров и констант из `kind: "Register"`/`"GlobalConstant"`
и выдать те, где владелец не свой и не в зависимостях. Прогонять после любой
пачки правок в проводках.

Отдельный сигнал: если в чужую константу лезут ТРИ независимые модели (так вышло
с `AmountScale`/`QuantityScale` у `Measurement`), значит не три потребителя
неправы, а константа лежит не в той модели — её место ниже, там, где её видят все.

## 5. Удаление объекта: только через API, и порядок задан правилом блокировки

Удаление файлов НЕ распространяется (кроме пунктов `Menu/menu.json`), поэтому
снос — это всегда «правка файлов ПЛЮС явный вызов API».

**Сначала сухой прогон.** `GET /api/metadata/delete-impact?objectType=Dictionary&id={metaId}`
возвращает `blockers`, `cascade`, `physicalTables`, **`dataRows`** и `canDelete`.
Он read-only и это ЕДИНСТВЕННЫЙ достоверный ответ на «есть ли за объектом
данные».

**«Ссылок в коде нет» — это НЕ «мёртв».** Реальный случай: справочник выглядел
полностью мёртвым по всему воркспейсу (ноль ссылок, пустая заглушка
обработчика), а в нём лежала настроенная строка, и читал его код ПЛАТФОРМЫ
(`ZuloOne.Core/Services/Integration/…`). Прежде чем сносить — `grep` ещё и по
`d:\Sources\zulo.one\src`, и посмотри `dataRows`.

**Порядок диктует одно правило:** EDT, указывающий на справочник, блокирует
удаление ТОЛЬКО пока им пользуется что-то СНАРУЖИ справочника — поле другого
справочника, поле шапки документа, измерение регистра или **свойство табличной
части**. Значит: сперва удаляешь поля-потребители, потом сам справочник, а EDT
уезжает каскадом вместе с ним. **EDT руками не удаляй** — `DELETE /api/metadata/edts/{id}`
ничем не защищён, и вызов не в том порядке даёт 500 от внешнего ключа.

```bash
B=http://localhost:5257/api
curl -s -X DELETE "$B/metadata/dictionaries/{dictId}/fields/{fieldId}"           # поле справочника
curl -s -X DELETE "$B/metadata/tableparttypes/{typeId}/properties/{propId}"      # свойство табличной части
curl -s -X DELETE "$B/metadata/dictionaries/{dictId}"                            # сам справочник (EDT каскадом)
curl -s -X DELETE "$B/metadata/numbersequences/{seqId}"                          # НЕ каскадится
curl -s -X DELETE "$B/metadata/menu/{itemId}"                                    # НЕ каскадится
```

Осиротевшие пункты меню ищутся по `targetMetaId` в `GET /api/metadata/menu`.

**После удаления поля маппинг сущности остаётся старым.** Колонку из таблицы
уже убрали, а вставка падает с `SqlException: Invalid column name '<Поле>'` —
до `docker restart zuloone-core-1`. Тот же класс несвежести, что у новых
справочников и полей документа.

Порядок проверки после сноса: restart → `schema/sync` → `models/compile` →
`tests/run-all`.

## 5. Проверка

`zuloone-verify` полностью, плюс специфика:
- после применения файлов — компиляция моделей И schema sync (новая колонка);
- живой смок: создать запись чужого объекта и убедиться, что твоё звено
  отработало (значение в поле-расширении);
- негативный тест: выключи свою модель — поле уходит из effective set,
  звено из цепочки; включи обратно;
- интеграционный тест на цепочку (порядок и данные) — раннер откатит данные
  сам (`zuloone-new-test`).
