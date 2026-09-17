---
name: zuloone-extend
description: Расширить ЧУЖУЮ модель ZuloOne — поля-расширения, CoC событий/сервисов/драйверов/команд/tx через next, прививка меню. Use when adding fields, event handlers, service wrappers, driver/command/tx wraps or menu items to objects owned by another model.
---

# Расширение чужой модели

Чужие модели не редактируются — они расширяются АДДИТИВНО из твоей модели.
Механики: поля-расширения, звено CoC (события, сервис, драйвер, команда,
tx), прививка меню.

## 0. Предусловия — без них расширение отклонят

1. **Зависимость объявлена**: ребро на модель-владельца в твоём `model.json`
   (скилл `zuloone-new-model`). Ссылка вне транзитивного замыкания
   зависимостей — нарушение depends-гейта.
2. **Слой строго выше** слоя расширяемой модели.
3. `Core/` расширяется как любая модель, но НЕ редактируется; `isSealed`-модель
   — тоже только расширяется.
4. **Бампни `modelVersion` СВОЕЙ модели** в том же изменении. Расширение
   живёт у тебя; тенант сравнивает версии и без бампа твою модель не
   перезальёт. Чужой `model.json` не трогай.

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
    "modelId": "<GUID ТВОЕЙ модели>" },
  "fields": [
    { "dictionaryMetaId": "<GUID чужого справочника>", "extensionMetaId": "<GUID-агрегата>",
      "fieldName": "CustomsCode", "name": "CustomsCode", "caption": "Код таможни",
      "baseType": "String", "length": 32, "displayOrder": 50, "isVisible": true,
      "metaId": "<GUID>", "modelId": "<GUID ТВОЕЙ модели>" }
  ]
}
```

Для документов — `DocumentExtensions/<Цель>.<Модель>/` тем же лекалом
(`targetDocumentTypeMetaId`). Поле живёт в ТВОЕЙ модели (row-слоение): отключение
твоей модели убирает поле из effective set. В сгенерированном классе сущности
поле появляется как обычное свойство — доступно всем скриптам.

## 2. Звено Chain of Command на чужом объекте

Обработчики одного объекта — **соседи**, не наследники (справочник,
документ, **табличная часть** `objectType: "TablePart"`). Список сортируется
по слою (потом имя): владелец слева / внутри, твоя модель справа / снаружи.
**Правое (верхний слой) стартует первым.** `next(...)` идёт вниз и возвращает
результат нижнего. Работа **после** `next()` видит, что записал владелец/GL —
тот же порядок побочек, что у старой подписки «сначала они, потом ты».

Звено — обычный EventHandler-скрипт твоей модели в папке агрегата:

`DictionaryExtensions/Country.WMS/CountryWmsEvents.script.json`:
```json
{
  "kind": "Script",
  "object": {
    "scriptType": "EventHandler", "objectType": "Dictionary",
    "objectMetaId": "<GUID чужого справочника>", "objectName": "Country",
    "extensionMetaId": "<GUID-агрегата>",
    "metaId": "<GUID>", "name": "CountryWmsEvents",
    "modelId": "<GUID ТВОЕЙ модели>" }
}
```

`CountryWmsEvents.cs` — класс с **УНИКАЛЬНЫМ именем** (не `CountryEventHandler` —
так уже зовётся звено владельца):

```csharp
#nullable enable
namespace ZuloOne.Runtime.Generated;

[ExtensionOf("Country")]                  // обязателен на чужом объекте, иначе ZOCOC004
public partial class CountryWmsEventHandler : TypedDictionaryEventHandler<Country>
{
    public override async Task<EventResult> OnBeforeSaveAsync(Country record, bool isNew, EventContext context)
    {
        var prior = await next(record, isNew, context);
        if (!prior.Success) return prior;
        // record и PreviousResult — уже после владельца.
        // Свои поля-расширения типизированы: record.CustomsCode.
        return EventResult.Ok();
    }
}
```

Правила: класс обязан нести `[ExtensionOf("<Цель>")]` — имя сверяется с конвертом
скрипта, иначе **ZOCOC004** (и он же, если атрибут повесить на обработчик СВОЕЙ
модели: это владелец); в `next` — те же аргументы, что у метода; забытый `next` —
**ZOCOC001**; `[Replace]` глотает цепочку намеренно; второй `next` в одном override
бросает; отключение модели снимает её звено. Владелец (обработчик в модели самого
объекта) — звено 0: он без атрибута и без `next`. `override` — против виртуала платформы,
не против чужого обработчика. `base.` / `super()` здесь не нужны и не работают
как CoC.

**Владелец — ровно один скрипт, и он уже создан.** При создании справочника или
документа платформа засеивает `<Имя>EventHandler` в модели объекта — это и есть
звено 0. Логику владельца пиши В НЁМ. Второй обработчик в СВОЕЙ же модели рядом —
не «ещё один владелец», а звено ВЫШЕ него: `next` обязателен (иначе ZOCOC001),
хотя `[ExtensionOf]` он не несёт — модель-то своя. Порядок звеньев —
`(владелец, потом слой, потом имя)`: до этого решали слой и имя, и
`SalesGLEventHandler` оказывался внутреннее `SalesInvoiceEventHandler` по алфавиту.

Полоска в дизайнере: владелец слева, ты справа; стрелки = направление `next`.
Вики: `wiki/developer/chain-of-command.md`.

## 2б. Сервис чужой модели — тот же `name`, `next<T>`

Чужой `IFoo` не наследуют и не подменяют новым именем, если нужна цепочка.
В своей модели заведи сервис с **тем же** `name` (контракт остаётся `IFoo`):
верхний слой стартует первым, `next<T>(аргументы)` вызывает нижний.

```csharp
public partial class Pricing
{
    public decimal PriceOf(Guid itemId, DateTime onDate)
        => next<decimal>(itemId, onDate) * 1.05m;
}
```

Асинхронно — `await nextAsync<decimal?>(...)`. Неиспользуемый метод контракта —
`=> next<T>(...)`. Проглотить нижний — `[Replace]` на методе. Забытый `next` —
**ZOCOC002** (реестр расширение не зарегистрирует, `GetService<IFoo>()` останется
на владельце). Две реализации на одном слое — ошибка. Класс по-прежнему зовётся
как сервис: в IDE-проекте воркспейса положи расширение в свой `namespace`.

В студии: **Сервисы** → карточка или скрипт — та же полоска, что у событий.
«Добавить свой поверх» создаёт сервис с тем же `name` в рабочей модели и
заглушки `next<T>`. Check скрипта ловит `ZOCOC002`.

Платформенный контракт (`IQuantityConverter`) — цепочка по слоям; ничья на
одном слое бросает, как раньше.

## 2в. Драйвер чужого регистра — `next` на хуке, не второй драйвер

У регистра один `TotalDriverMetaId`. Второй драйвер рядом поставить нельзя.
Обёртка — скрипт твоей модели с `scriptType: "TotalDriver"`, тем же
`objectMetaId`. Класс уникален, база `TotalDriverLayerBase` генерится.

**Kernel-драйвер** (`StockBaseTotalDriver`, `FifoTotalDriver`, …) — владелец
уже скомпилирован в Core. Owner-partial `class StockBaseTotalDriver` в своей
модели писать нельзя: это тот же CLR-класс. Полоска показывает движок слева;
«Добавить свой поверх» создаёт wrap с `next()`, не второй owner.

```csharp
public partial class StockWmsTotalDriver
{
    public decimal CalculatePartialAmount(decimal lotQuantity, decimal lotAmount, decimal transQuantity)
        => next<decimal>(lotQuantity, lotAmount, transQuantity);
}
```

Владелец по-прежнему зовёт `base.` движка. Забытый `next` — **ZOCOC003**.

## 2г. Команда чужой модели — луковица `ExecuteAsync`

Одна мета-команда (`PlaceOrder` в Inventory). Вторую «с тем же смыслом»
не заводить. Обёртка на полоске этой команды:

```csharp
public partial class PlaceOrderWmsCommand
{
    public override async Task<CommandResult> ExecuteAsync(CommandContext context)
    {
        var prior = await nextAsync<CommandResult>(context);
        if (!prior.Success) return prior;
        return prior;
    }
}
```

`[Replace]` не зовёт владельца (и его переход подтипа). **ZOCOC003**, если
забыл `next`. Рабочая модель строго выше слоем.

## 2д. Tx чужого скрипта — вертикаль, список подтипа не трогать

Две оси: склад и себестоимость — **разные** owner-скрипты на подтипе
(горизонталь, `executionOrder`). CoC — когда нужно поправить **уже
существующий** `ReceiptTx`, а не поставить ещё один tx рядом.

```csharp
public partial class ReceiptTxWms
{
    public override void GetTransactions(DocumentContext document, TransactionPairCollection pairs, TransactionCollection transactions)
    {
        next(document, pairs, transactions);
        // коллекции уже с движениями владельца
    }
}
```

«Добавить свой поверх» на полоске **этого** tx ≠ «новый tx на подтипе».
Costing и склад не склеиваются в одну луковицу.

## 3. Меню и прочее

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

Перенос делается БЕЗ пересоздания: тот же `metaId`, новый `modelId` и
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
