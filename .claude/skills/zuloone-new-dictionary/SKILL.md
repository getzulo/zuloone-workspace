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
