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
    "isActive": true,
    "groupName": "Бизнес-слой.<Контур>",
    "metaId": "<GUID>", "name": "<Имя>",
    "modelId": "<GUID модели>" }
}
```

**`groupName` обязателен** — без него тест виден только в «Все тесты», и список
из сотни имён перестаёт читаться. Формат — `Раздел.Подраздел`, страница «Тесты»
рисует по нему дерево слева.

Ось группировки для бизнес-слоя — **учётный контур, а не модуль**: колонка
«Модель» в списке уже показывает модуль, и группа по модулю просто продублировала
бы её. Контур же идёт ПОПЕРЁК моделей — налоговый живёт в `Tax` + `Sales` +
`Purchasing` + `LocalizationSaudiArabia` сразу, и увидеть его целиком по колонке
«Модель» нельзя.

Действующие разделы (пополняй, а не плоди синонимы):

| Группа | Что туда |
|---|---|
| `Бизнес-слой.Товародвижение` | приход, склад, производство, себестоимость |
| `Бизнес-слой.Продажи и расчёты` | отгрузка, выручка, дебиторка, лояльность |
| `Бизнес-слой.Налоги` | расчёт, леджер, входной/выходной, локализации |
| `Бизнес-слой.Кадры и ФОТ` | табель, начисление, выплата, отчисления |
| `Бизнес-слой.Главная книга` | план счетов и разноска подсистем в GL |
| `Бизнес-слой.Сервисы` | переиспользуемые расчёты (`I<Имя>`) |
| `Бизнес-слой.Команды` | управляемые переходы состояния |
| `Бизнес-слой.Инварианты` | правила, которые модель держит вне зависимости от сценария |

`Ядерные тесты.*` — платформенные (модель TestBench), туда бизнес-тесты не
кладём; `Auto tests` проставляет генератор сам.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZuloOne.Runtime.Testing;
// Тесту глобальные usings НЕ подставляются — см. раздел про коллизии имён.

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
| Регистр сведений | `SetInformationAsync / SliceLastAsync / SliceFirstAsync / QueryInformationAsync / GetInformationAsync / DeleteInformationAsync` (есть и типизированные `SetInformationAsync<T>` / `SliceLastAsync<T>`) |
| Связи документов | `AddDocumentLinkAsync / GetDocumentFamilyEdgesAsync` |
| Команды | `FindCommandIdAsync + Execute…CommandAsync` |
| Сервисы | `GetService<T>()` — корневые менеджеры и контракты `I<Имя>`, как в любом скрипте, но `using` пиши сам (см. ниже) |
| Точки отката | `SavepointAsync / RollbackToSavepointAsync` |

**Тест НЕ получает глобальных usings.** `RuntimeCompiler` подставляет
`GlobalUsings.cs` только партиальным скриптам (`IsPartialScript`), а тест —
обычный класс `: IntegrationTestScriptBase`. Обработчик события видит
`IDataService` без единого `using`; тест на том же имени получит `CS0246`, хотя
`ZuloOne.Core` в ссылках есть. Каждое пространство имён — руками, в файле.

Отсюда же коллизии имён, и они опаснее, чем кажется:

| Имя | Брать отсюда | Что ещё лежит под тем же именем |
|---|---|---|
| `IDocumentManager` | `ZuloOne.Managers` | легаси на `long`-идентификаторах в `ZuloOne.Core.Services` |
| `IMetadataService` | `ZuloOne.Core.Services` | легаси в `ZuloOne.Metadata` и ещё одно в `ZuloOne.Server.Metadata` |
| контракты моделей `I<Имя>` | `ZuloOne.Services.Contracts` | — |

`IDocumentManager` из чужого namespace даёт `CS0246` — заметно сразу. С
`IMetadataService` хуже: `using ZuloOne.Metadata;` РАЗРЕШИТ имя, но в легаси-
интерфейс без нужных `Get*Async`, и ошибка будет `CS1061: does not contain a
definition for GetWebServiceAsync` — читается как «в платформе нет метода» и
уводит чинить платформу. Прежде чем дописывать ядро по такому `CS1061` —
проверь, к какому из трёх интерфейсов привязалось имя.

```csharp
using ZuloOne.Core.Services;   // IDataService, ISqlService, IMetadataService
using ZuloOne.Managers;        // IDocumentManager, IDictionaryManager
using ZuloOne.Services.Contracts;

private static IDocumentManager DocumentManager => GetService<IDocumentManager>();
```

Проверяй РЕЗУЛЬТАТ, а не возврат метода: если сервис создаёт документ, перечитай
его `GetDocumentAsync<T>(id)` и сверяй поля и строки документа — иначе тест
подтверждает лишь то, что сервис что-то посчитал, но не то, что это сохранилось.

`Assert.IsTrue(условие, "сообщение с {0}", значение)`; провал кейса не мешает
остальным.

## Запуск

```bash
curl -s -X POST http://localhost:5257/api/metadata/tests/run-all      # все
curl -s -X POST http://localhost:5257/api/metadata/tests/<GUID>/run   # один
```

Результат — по кейсам: Passed/Failed/Skipped, длительность, аллокации,
ошибка компиляции скрипта, если есть. Страница «Тесты» показывает то же.

## Убедись, что тест УМЕЕТ ПАДАТЬ

Зелёный тест доказывает работу защиты только если он краснеет, когда защиту
убрать. Тест, который не может упасть, хуже отсутствующего: он читается как
покрытие.

Проверка стоит одной минуты — временно сними проверяемое условие, прогони,
верни:

```bash
# закомментировать строку защиты → apply-file → прогнать ОДИН тест
# ожидание: «Assert.IsTrue failed. заданий раскладки ровно одно, факт 2»
# затем вернуть строку и убедиться, что снова зелено
```

Делай это ОБЯЗАТЕЛЬНО, когда тест прошёл с первого раза, а проверяемое условие
нетривиально: совпадение может означать и работающую защиту, и то, что
проверяемая ситуация в тесте вообще не наступает.

Так была поймана собственная ошибка: тест «приход не плодит два задания» сначала
создавал товар с партией себестоимости — по аналогии с продажами, где именно
партия заставляет событие сработать дважды. Но приход склад УВЕЛИЧИВАЕТ, драйвер
себестоимости срабатывает на чистом минусе, вторичных движений нет и события
второго не было. Тест был зелёным и пустым. Настоящий источник повтора у прихода
— перепроведение; переписанный на него тест со снятой защитой честно показал
«факт 2».

Сюда же: **тест на побочный эффект проведения обязан заводить остаток ПРИХОДОМ,
а не `PostMovementAsync("Stock")`** — прямое движение не запускает драйверы, и
целый класс ошибок (удвоение разноски, списание себестоимости) в таком тесте
физически не воспроизводится.

## Чего в тестах НЕ делать

- уповать на «вчерашние» данные стенда или порядок тестов;
- проверять побочные эффекты вне транзакции (файлы, внешние вызовы) —
  их и не должно быть в скриптах;
- плодить метаданные без нужды — метаданные создавай в модели файлами,
  тестом — только данные;
- писать пары равных чисел там, где проверяется РАЗДЕЛЕНИЕ (7 и 3 в разных
  ячейках, а не 5 и 5): при схлопывании одинаковые числа дадут правдоподобный
  результат, и ошибка пройдёт незамеченной.
