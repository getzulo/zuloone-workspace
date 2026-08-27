---
name: zuloone-new-document
description: Создать новый документ ZuloOne — шапка, табличная часть, ПОДТИПЫ-СОСТОЯНИЯ с переходами, транзакционные скрипты проводок, порождение связанных документов. Use when adding a business document (order, receipt, invoice) to a ZuloOne model.
---

# Новый документ

## Модель состояний — сначала пойми это

**Подтип (subtype) — это ТЕКУЩЕЕ СОСТОЯНИЕ документа.** Документ живёт как
конечный автомат: `Черновик → Заказано → Оприходовано → …`. Переход между
подтипами — бизнес-событие:

- движок реверсит движения текущего состояния и исполняет транзакционную
  цепочку ЦЕЛЕВОГО подтипа — проводки всегда отражают текущее состояние;
- откат назад (в подтип без скриптов) просто снимает проводки;
- скрипты и события перехода могут менять связанную систему — в том числе
  ПОРОЖДАТЬ другие документы: заказ на покупку при переходе «Заказано →
  Оприходовано» создаёт документ прихода по заказанным строкам и проводит
  его, что порождает складские движения, и так далее по цепочке.

**Статусы (`statuses`) НЕ заводить.** Это легаси-механизм, дублирующий
подтипы, — он будет удалён из сущности документа. Состояние = подтип,
проведённость = `postOnSave: true` (сохранённый документ проведён в своём
текущем подтипе).

## 1. Тип табличной части — `TableParts/<Документ>Lines.json`

```json
{
  "kind": "TablePartType",
  "object": {
    "caption": "Строки", "tableName": "TP_<Документ>Lines",
    "className": "<Документ>LinesTablePartRow",
    "softDeletion": false, "isLogged": false,
    "metaId": "<GUID-типа-строк>", "name": "<Документ>Lines",
    "modelId": "<GUID модели>", "layerId": 1
  },
  "properties": [
    { "tablePartTypeMetaId": "<GUID-типа-строк>", "fieldName": "Item", "name": "Item",
      "edtMetaId": "<GUID RefItem>", "isRequired": true, "displayOrder": 1, "isVisible": true,
      "metaId": "<GUID>", "modelId": "<GUID модели>", "layerId": 1 },
    { "tablePartTypeMetaId": "<GUID-типа-строк>", "fieldName": "Quantity", "name": "Quantity",
      "baseType": "Decimal", "precision": 18, "scale": 4, "displayOrder": 2, "isVisible": true,
      "metaId": "<GUID>", "modelId": "<GUID модели>", "layerId": 1 }
  ]
}
```

## 2. Документ — `Documents/<Имя>/<Имя>.object.json`

```json
{
  "kind": "Document",
  "object": {
    "caption": "<English caption>", "caption_ru": "<Русская подпись>", "requiresPosting": true, "postOnSave": true,
    "isCloneable": true, "numberSequenceMetaId": "<GUID-серии>",
    "metaId": "<GUID-документа>", "name": "<Имя>",
    "modelId": "<GUID модели>", "layerId": 1
  },
  "headerFields": [
    { "documentTypeMetaId": "<GUID-документа>", "fieldName": "Warehouse", "name": "Warehouse",
      "edtMetaId": "<GUID RefWarehouse>", "isRequired": true, "displayOrder": 1, "isVisible": true,
      "metaId": "<GUID>", "modelId": "<GUID модели>", "layerId": 1 }
  ],
  "subtypes": [
    { "documentTypeMetaId": "<GUID-документа>", "name": "Draft", "subtypeValue": "Draft",
      "subtypeCaption": "Черновик", "displayOrder": 1,
      "metaId": "<GUID>", "modelId": "<GUID модели>", "layerId": 1 },
    { "documentTypeMetaId": "<GUID-документа>", "name": "Ordered", "subtypeValue": "Ordered",
      "subtypeCaption": "Заказано", "displayOrder": 2,
      "metaId": "<GUID-подтипа-Ordered>", "modelId": "<GUID модели>", "layerId": 1 },
    { "documentTypeMetaId": "<GUID-документа>", "name": "Received", "subtypeValue": "Received",
      "subtypeCaption": "Оприходовано", "displayOrder": 3,
      "metaId": "<GUID-подтипа-Received>", "modelId": "<GUID модели>", "layerId": 1 }
  ],
  "tableParts": [
    { "documentTypeMetaId": "<GUID-документа>", "tablePartTypeMetaId": "<GUID-типа-строк>",
      "name": "Lines", "isCloneable": true,
      "metaId": "<GUID>", "modelId": "<GUID модели>", "layerId": 1 }
  ]
}
```

- `ID`, номер и дата — системные, не объявляй.
- `displayOrder` подтипов = порядок workflow (движок различает переход
  вперёд/назад по нему). Первый подтип — начальное состояние; обычно без
  скриптов = черновик без движений.

## 3. Проводки состояния — `Documents/<Имя>/Transactions/*.*`

У подтипа может быть СКОЛЬКО УГОДНО транзакционных скриптов — по одному на
цель: складские движения, себестоимость, финансовые проводки, резервы…
При проведении состояния исполняется ВСЯ цепочка скриптов подтипа по
`executionOrder` (при равенстве — по времени создания); итог = объединение
движений всех скриптов. Дели по ответственности: один скрипт — один регистр
или одна учётная цель, так их можно менять и расширять независимо.

Каждый скрипт — своя пара файлов; `objectMetaId` = GUID ПОДТИПА:

```json
{
  "kind": "Script",
  "object": {
    "scriptType": "TransactionScript", "objectType": "Document",
    "objectMetaId": "<GUID подтипа>", "objectName": "<Имя документа>",
    "executionOrder": 1,
    "metaId": "<GUID-скрипта>", "name": "<Имя><Подтип><Цель>Tx",
    "modelId": "<GUID модели>", "layerId": 1
  }
}
```

`.cs` — БЕЗ объявления базового класса (framework генерится); имя класса
уникально во всём воркспейсе. Пример пары скриптов одного подтипа:
`ReceiptStockTx` (executionOrder 1 — складские остатки) и `ReceiptCostTx`
(executionOrder 2 — себестоимость):

```csharp
public partial class <Имя><Подтип><Цель>Tx
{
    protected override void GetTransactions(<Имя> document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        foreach (var line in document.Lines)
        {
            transactions.Add(new <Регистр> { Warehouse = document.Warehouse, Item = line.Item ?? Guid.Empty, Quantity = line.Quantity ?? 0m });
            // двойная запись: transactionPairs.Add(минус, плюс);
            // динамическая аналитика: new RegisterMovementSpec("<Регистр>").Dim(...).An(...).Res(...)
        }
    }
}
```

`GetTransactions` — ЧИСТЫЙ расчёт движений, без побочных эффектов. Логика
перехода (создать связанный документ, дернуть сервис) — в событиях (шаг 4).

## 4. События перехода — `Events/<Имя>EventHandler.*`

База `TypedDocumentEventHandler<<Имя>>`. Ключевые хуки:

- `OnBeforePostAsync` — валидация перед проведением состояния;
  `EventResult.Cancel("причина")` блокирует переход;
- `OnAfterPostAsync` — состояние проведено: здесь порождаются связанные
  документы и прочие последствия перехода;
- `OnBeforeUnpostAsync` / `OnAfterUnpostAsync` — снятие движений при уходе
  из состояния.

Порождение связанного документа (заказ → приход):

```csharp
public override async Task<EventResult> OnAfterPostAsync(PurchaseOrder header, EventContext context)
{
    if (header.Subtype != "Received") return EventResult.Ok();

    var docs = context.GetService<IDocumentManager>();
    var receipt = new GoodsReceipt { Warehouse = header.Warehouse };
    var order = await docs.GetDocumentAsync<PurchaseOrder>(header.MetaId);
    foreach (var line in order!.Lines)
        receipt.Lines.Add(new GoodsReceiptLinesTablePartRow { Item = line.Item, Quantity = line.Quantity });

    await docs.SaveDocumentAsync(receipt);              // postOnSave: приход проведётся сам
    await docs.AddLinkAsync(header.MetaId, receipt.MetaId); // родословная документов
    return EventResult.Ok();
}
```

В любом скрипте доступны СЕРВИСЫ — платформенные (`IDocumentManager`,
`IDictionaryManager`, `ILinkTableManager`, `IDataService`,
`IRegisterMovementService`, `IConstantResolver`, … — полный перечень в
CLAUDE.md) и свои (скилл `zuloone-new-service`):
`context.GetService<T>()` в событиях/командах, `GetService<T>()` — в любом
partial-скрипте.

**Тела событий держи тонкими.** Повторяющиеся операции (как генерация
прихода выше, если она нужна ещё где-то) оборачиваются в сервис — событие
только решает КОГДА, сервис знает КАК. Пороги и коды режимов не хардкодятся —
глобальные константы (`GlobalConstants.<Группа>.<Имя>`) или константы
документа; правишь значение — код не трогаешь.

## 5. Переход состояния снаружи

UI двигает подтип в форме документа; программно — `SaveDocumentAsync` с
изменённым `Subtype` (движок сам реверсит и пересеивает) или API
`POST /api/documents/{typeId}/{docId}/subtype`.

Это НЕОХРАНЯЕМЫЙ переход — годится для системных/событийных переводов, где
условия уже проверены выше по цепочке. Если переход должен явно проверять
готовность (есть строки, хватает остатка…) и давать пользователю кнопку —
это **команда**, см. скилл `zuloone-new-command`.

## 6. Меню + проверка

Пункт `targetType: "Document"` в `Menu/menu.json`, `parentMetaId` — GUID
подгруппы **`Documents`/«Документы»** модели (`zuloone-new-model`), не
корневой группы напрямую. Затем `zuloone-verify` + интеграционный тест на
ПЕРЕХОДЫ: создать → перевести по состояниям → остатки регистров сходятся на
каждом шаге, включая откат назад.
