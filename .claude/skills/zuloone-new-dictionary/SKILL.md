---
name: zuloone-new-dictionary
description: Создать новый справочник ZuloOne в воркспейсе — EDT, номерная серия, object.json, обработчик событий, пункт меню, проверка. Use when adding a dictionary (catalog/reference table) to a ZuloOne model.
---

# Новый справочник

Все файлы создаются в папке СВОЕЙ модели (`<Модель>/…`). `modelId` каждой
строки = `metaId` из `<Модель>/model.json`, `layerId` — оттуда же. Для каждого
нового объекта/строки генерируй СВОЙ GUID (uuid v4). Имена — PascalCase латиницей.

## 1. Номерная серия — `NumberSequences/<Имя>Seq.json`

```json
{
  "kind": "NumberSequence",
  "object": {
    "padLength": 0, "startValue": 1000, "increment": 1, "nextValue": 1000,
    "resetPolicy": "None",
    "metaId": "<GUID-серии>", "name": "<Имя>Seq",
    "modelId": "<GUID модели>", "layerId": 1
  }
}
```

## 2. Справочник — `Dictionaries/<Имя>/<Имя>.object.json`

```json
{
  "kind": "Dictionary",
  "object": {
    "caption": "<English caption>",
    "caption_ru": "<Русская подпись>",
    "caption_ar": "<الترجمة العربية>",
    "description": "",
    "isHierarchical": false,
    "numberSequenceMetaId": "<GUID-серии>",
    "defaultSearchProperty": "Name",
    "displayFormat": "{ID} - {Name}",
    "isLogged": false, "isVersioned": false, "isCached": false,
    "metaId": "<GUID-справочника>", "name": "<Имя>",
    "modelId": "<GUID модели>", "layerId": 1
  },
  "fields": [
    {
      "dictionaryMetaId": "<GUID-справочника>",
      "fieldName": "Name", "name": "Name", "caption": "Name", "caption_ru": "Наименование",
      "baseType": "String", "length": 256,
      "isRequired": true, "displayOrder": 1, "isVisible": true,
      "metaId": "<GUID-поля>", "modelId": "<GUID модели>", "layerId": 1
    }
  ]
}
```

- Системное поле `ID` НЕ пиши — его (и `ParentId` для `isHierarchical: true`)
  создаст сервер при применении файла.
- Скалярное поле: `baseType` `String|Integer|Long|Decimal|DateTime|Boolean|Guid`
  (+ `length` / `precision`+`scale`). Целое — `Integer`, НЕ `Int`: импорт `Int`
  проглотит молча, а споткнётся `schema/sync` («Base type 'Int' is not in the
  catalog») — объект в базе уже есть, таблицы под него нет.
- **Необязательный `Boolean` генерится НЕ-nullable**, то есть незаполненный
  признак равен `false`. Поэтому имя флага выбирается так, чтобы РАБОЧИМ было
  именно `false`: `IsDisabled`, а не `IsActive`. Иначе запись, заведённая без
  галки, молча не участвует в логике — а выглядит нормальной. Проверять это
  событием-умолчанием нельзя: `NewRecord<T>()` строится в памяти вызывающего, и
  запись, собранная кодом или API, до события не доходит.
- Ссылка на другой справочник: сначала EDT (шаг 3), затем в поле `edtMetaId`
  вместо `baseType`.
- Один `fieldName` — один раз.
- Подписи: `caption` — английский, `caption_ru`/`caption_ar` — переводы
  (у любого узла с metaId: справочника, поля, пункта меню…).

## 2а. Коэффициент, зависящий от другой сущности, — отдельный справочник

Частая и дорогая ошибка: положить коэффициент на справочник, для которого он на
самом деле не постоянен. «Коробка = 12 штук» на единице измерения выглядит
компактно, а означает «у ВСЕЙ номенклатуры коробка по 12» — и у второго товара
цифра врёт. Признак: чтобы ответить на вопрос «сколько», нужна ВТОРАЯ сущность.

Такое значение живёт в справочнике-связке `(A, B) → число` с уникальностью пары.
Проверку уникальности пиши в обработчике: без неё две записи дают два разных
ответа на один вопрос, и результат начинает зависеть от того, какая строка
попалась первой.

Зеркальный приём для НАБОРА величин: вместо попарных правил `(из, в, множитель)`
заведи класс и коэффициент к базовому элементу. Тогда N чисел заменяют N²
правил, переход «через третий элемент» считается сам, а противоречивую тройку
становится нечем выразить. Попарная таблица, наоборот, требует руками заводить
транзитивные пары и молча допускает несогласованность.

## 2б. Само-ссылающийся справочник и защита от циклов

Справочник, у которого поле ссылается на СВОЙ ЖЕ справочник (тип цены,
вычисляемый от другого типа цены; категория от родительской категории и т.п.,
если это не встроенная иерархия `isHierarchical` с системным `ParentId`, а
своё поле со своей семантикой) — обычный ссылочный EDT (шаг 3), где
`referenceDictionaryMetaId` указывает на ТОТ ЖЕ справочник. Ничего особого в
метаданных нет. Особое — в обработчике: обход цепочки обязан ловить и прямой
self-reference (поле ссылается на собственную запись), и транзитивный цикл
(A→B→A и длиннее), иначе рекурсивное разрешение значения в сервисе рано или
поздно уйдёт в бесконечный цикл или `StackOverflow`.

```csharp
if (record.BasePriceType == Guid.Empty)
    return EventResult.Cancel("...");

var visited = new HashSet<Guid> { record.MetaId };
var currentId = record.BasePriceType;
var depth = 0;
while (true)
{
    if (!visited.Add(currentId))
        return EventResult.Cancel("Цепочка зациклилась");
    if (++depth > MaxChainDepth)
        return EventResult.Cancel($"Цепочка длиннее {MaxChainDepth} уровней");

    var current = await manager.GetRecordAsync(currentId);
    if (current == null || /* дошли до терминального узла */)
        break;
    currentId = current.BasePriceType;
}
```

**Ловушка, из-за которой прямая проверка `record.BasePriceType == record.MetaId`
и сид `visited = { record.MetaId }` НЕ ловят self-reference на первом же
сохранении новой записи.** У новой (`isNew: true`) записи `record.MetaId`
внутри `OnBeforeSaveAsync`/`OnBeforeInsertAsync` — это НЕ тот Guid, что видит
вызывающий код, и НЕ тот, что в итоге попадёт в БД. Платформа успевает
пересобрать сущность как минимум дважды между `NewRecord<T>()` и записью в
таблицу: `SaveRecordAsync` уносит поля в словарь БЕЗ `MetaId` (системное поле
исключено намеренно), `DataService.InsertAsync` минтит РЕАЛЬНЫЙ id ПОСЛЕ
вызова before-хука, а сам объект `record`, который видит хук, материализован
`EntityMarshaler.ToEntity<T>` заново через `Activator.CreateInstance` —
конструктор справочника при этом молча присваивает ЕЩЁ один, одноразовый
`MetaId`, никак не связанный ни с тем, что держит вызывающий код, ни с тем, что
в итоге сохранится. Три разных Guid на одну запись, и все три — не ошибка,
а нормальная работа конвейера сохранения.

Практическое следствие: **само-ссылку в принципе нельзя (и не нужно) ловить
на самой первой вставке** — обход цепочки, сидящий `record.MetaId`-ом, при
любой попытке проверить её на `isNew` будет сравнивать несвязанные Guid'ы и
молча пропустит невалидную запись. Но это не дыра в конкретной проверке, а
отражение реальности: сослаться на ещё не существующую запись всё равно
неоткуда — справочник-пикер в UI не может выбрать то, чего ещё нет в базе.
Само-ссылка физически достижима только ОБНОВЛЕНИЕМ уже сохранённой записи
(создал без ссылки → сохранил → получил настоящий `MetaId` → отредактировал,
подставив ссылку на самого себя → сохранил снова) — и на этом пути
`record.MetaId` уже стабилен и совпадает с тем, что хранится в БД, так что тот
же самый обход, что ловит A→B→A, корректно ловит и вырожденный цикл длины 1.
Пиши интеграционный тест на self-reference по образцу теста на транзитивный
цикл — с обязательным промежуточным сохранением, а не подстановкой ссылки на
себя до первого `SaveRecordAsync`.

## 3. Ссылочный EDT (если нужен) — `EDTs/Ref<Цель>.json`

```json
{
  "kind": "EDT",
  "object": {
    "edtType": "Reference",
    "referenceDictionaryMetaId": "<GUID целевого справочника>",
    "metaId": "<GUID-EDT>", "name": "Ref<Цель>",
    "modelId": "<GUID модели>", "layerId": 1
  }
}
```

## 4. Модуль событий — `Dictionaries/<Имя>/Events/<Имя>EventHandler.*`

Платформа создаёт пустой модуль сама при появлении объекта; если пишешь логику
сразу — пара файлов:

`<Имя>EventHandler.script.json`:
```json
{
  "kind": "Script",
  "object": {
    "scriptType": "EventHandler", "objectType": "Dictionary",
    "objectMetaId": "<GUID-справочника>", "objectName": "<Имя>",
    "metaId": "<GUID-скрипта>", "name": "<Имя>EventHandler",
    "modelId": "<GUID модели>", "layerId": 1
  }
}
```

`<Имя>EventHandler.cs` — типизированный partial (полный список хуков — в
существующих модулях и `.generated/Frameworks/`):
```csharp
#nullable enable
namespace ZuloOne.Runtime.Generated;

public partial class <Имя>EventHandler : TypedDictionaryEventHandler<<Имя>>
{
    public override Task<EventResult> OnBeforeSaveAsync(<Имя> record, bool isNew, EventContext context)
    {
        // if (string.IsNullOrEmpty(record.Name)) return Task.FromResult(EventResult.Cancel("Наименование обязательно"));
        return Task.FromResult(EventResult.Ok());
    }
}
```

## 5. Пункт меню — `Menu/menu.json` своей модели

Добавь пункт в `items` (файл авторитетный: не удаляй чужого, не трогай чужие
`metaId`). Без `templateMetaId` = кусок меню модели, виден всем.
`parentMetaId` — GUID подгруппы **`Dictionaries`/«Справочники»** модели (см.
`zuloone-new-model`), не корневой группы модели напрямую; если такой
подгруппы в модели ещё нет — заведи её:

```json
{ "kind": "Menu", "items": [
  { "name": "<Имя>", "caption": "<English>", "caption_ru": "<Русская>", "targetType": "Dictionary",
    "targetMetaId": "<GUID-справочника>", "parentMetaId": "<GUID подгруппы Dictionaries>",
    "displayOrder": 1, "metaId": "<GUID-пункта>", "modelId": "<GUID модели>", "layerId": 1 }
] }
```

## 6. Проверка — обязательно

Скилл `zuloone-verify`: синк применил → компиляция Ok → схема синхронизирована →
интеграционный тест на создание/чтение записи.
