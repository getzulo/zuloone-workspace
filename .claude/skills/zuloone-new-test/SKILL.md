---
name: zuloone-new-test
description: Написать интеграционный тест ZuloOne — файловая пара, харнес Db, автоматический откат данных (тесты чистят за собой сами). Use when covering a business scenario (document flow, postings, chains, services) with an integration test.
---

# Новый интеграционный тест

Каждый бизнес-сценарий модели покрывается интеграционным тестом. Тесты
исполняются НА СЕРВЕРЕ против живой метамодели.

## Главное: тесты чистят за собой САМИ

Каждый тест-кейс раннер исполняет внутри транзакции, которая **никогда не
коммитится** — всё, что тест создал (записи, документы, движения), откатывается
автоматически. Поэтому:

- НЕ пиши ручную уборку данных (`DeleteAsync` в конце) — она не нужна;
- НЕ полагайся на данные, созданные другим тестом или прошлым прогоном;
- всё нужное сценарию тест создаёт сам, в начале;
- `Db.SavepointAsync("имя")` / `RollbackToSavepointAsync` — промежуточные
  точки внутри кейса; `CountCommittedAsync` смотрит СКВОЗЬ транзакцию
  (что реально в базе).

Исключение — МЕТАДАННЫЕ, созданные тестом (`Db.CreateDictionaryAsync`,
`SyncSchemaAsync`): DDL может пережить откат — используй их только когда тест
именно про метаданные, и убирай `CascadeDeleteMetadataAsync` в конце кейса
(поддерживает Dictionary, DocumentType, Register, LinkTable).

## Файлы — `Tests/<Имя>.json` + `Tests/<Имя>.cs`

```json
{
  "kind": "Test",
  "object": {
    "description": "<Что покрывает>",
    "metaId": "<GUID>", "name": "<Имя>",
    "modelId": "<GUID модели>", "layerId": 1
  }
}
```

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;

public class GoodsFlowTest : IntegrationTestScriptBase
{
    [IntegrationTest("Приход увеличивает остаток склада")]
    public async Task ReceiptIncreasesStock()
    {
        // 1. Данные сценария — создаём сами.
        var wh = await Db.InsertAsync("Warehouse", new Dictionary<string, object?> { ["Name"] = "Тест-склад" });
        var item = await Db.InsertAsync("Item", new Dictionary<string, object?> { ["Name"] = "Тест-товар" });

        // 2. Документ с табличной частью, в целевом состоянии.
        var doc = await Db.CreateDocumentAsync("GoodsReceipt",
            new Dictionary<string, object?> { ["Warehouse"] = wh },
            new Dictionary<string, IEnumerable<IDictionary<string, object?>>>
            {
                ["Lines"] = new[] { new Dictionary<string, object?> { ["Item"] = item, ["Quantity"] = 5m } },
            },
            subtype: "Received");

        // 3. Остатки регистра.
        var balances = await Db.QueryBalancesAsync("StockBalance", $"Warehouse = '{wh}'");
        Assert.IsTrue(balances.Count == 1, "одна строка остатка, а не {0}", balances.Count);
        Assert.IsTrue(Convert.ToDecimal(balances[0]["Quantity"]) == 5m,
            "остаток 5, а не {0}", balances[0]["Quantity"]);

        // 4. Переход состояния назад снимает движения.
        await Db.ChangeSubtypeAsync("GoodsReceipt", doc, "Draft");
        Assert.IsTrue((await Db.QueryMovementsAsync("StockBalance", $"Warehouse = '{wh}'")).Count == 0,
            "откат состояния снял движения");
    }
}
```

Один класс — несколько `[IntegrationTest]`-кейсов; каждый кейс — своя
транзакция. Класс объявляет базу `IntegrationTestScriptBase` сам; имя класса
уникально во всём воркспейсе.

## Харнес `Db` — основное

| Задача | Вызов |
|---|---|
| Записи | `InsertAsync / GetAsync / UpdateAsync / DeleteAsync / QueryAsync / CountAsync` |
| Типизированная запись | `GetRecordAsync<Item>(id)` |
| Документ + строки + состояние | `CreateDocumentAsync(type, header, tableParts, subtype)` |
| Переход состояния | `ChangeSubtypeAsync(type, docId, "Received")` |
| Движение напрямую | `PostMovementAsync(register, date, dims, resources)` |
| Остатки / движения | `QueryBalancesAsync / QueryMovementsAsync(register, filter)` |
| Связи документов | `AddDocumentLinkAsync / GetDocumentFamilyEdgesAsync` |
| Команды | `FindCommandIdAsync + Execute…CommandAsync` |
| Сервисы | `GetService<T>()` — корневые менеджеры и контракты `I<Имя>`, как в любом скрипте; в самодостаточном тесте добавь `using ZuloOne.Core.Services;` |
| Точки отката | `SavepointAsync / RollbackToSavepointAsync` |

`Assert.IsTrue(условие, "сообщение с {0}", значение)`; провал кейса не мешает
остальным.

## Запуск

```bash
curl -s -X POST http://localhost:5257/api/metadata/tests/run-all      # все
curl -s -X POST http://localhost:5257/api/metadata/tests/<GUID>/run   # один
```

Результат — по кейсам: Passed/Failed/Skipped, длительность, аллокации,
ошибка компиляции скрипта, если есть. Страница «Тесты» показывает то же.

## Чего в тестах НЕ делать

- уповать на «вчерашние» данные стенда или порядок тестов;
- проверять побочные эффекты вне транзакции (файлы, внешние вызовы) —
  их и не должно быть в скриптах;
- плодить метаданные без нужды — метаданные создавай в модели файлами,
  тестом — только данные.
