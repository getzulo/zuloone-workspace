---
name: zuloone-new-register
description: Создать регистр накопления ZuloOne — ресурсы, ДИНАМИЧЕСКИЕ АНАЛИТИКИ из переиспользуемого каталога, движок/драйвер итогов; §7 — кросс-регистровые отчёты через Virtual Total. Use when adding an accumulation register (stock, money, cost) to a ZuloOne model.
---

# Новый регистр

Регистр = таблица движений (TR_) + остатки. Ресурсы (resources) — накапливаемые
числа. Разрезы учёта — **динамические аналитики**: значения не становятся
колонками TR_, движение ссылается на иммутабельный набор значений одним id.

## Конвенция разрезов: ВСЕ аналитики — динамические

- **Физические измерения (`dimensions`) НЕ заводить.** Привязка/отвязка
  аналитики — чистая правка метаданных: без миграции схемы, без потери
  данных; отчёты режут и фильтруют по аналитикам так же.
- **Аналитика — переиспользуемая**: живёт в ГЛОБАЛЬНОМ каталоге
  (`Analytics/<Имя>.json`), объявляется ОДИН раз и привязывается к любому
  числу регистров любых моделей. Прежде чем заводить новую — посмотри каталог:
  базовые (`Country`, `Region`, `City`, `Currency`, `UnitOfMeasure`, …) уже
  едут с моделью Common.
- **Аналитика связана со своим справочником** через Reference-EDT
  (`edtMetaId` → `Ref<Справочник>`): значения — ссылки на записи, отчёты
  резолвят подписи. Документная аналитика — `referencedDocumentTypeMetaId`.

## 1. Аналитика (если в каталоге ещё нет) — `Analytics/<Имя>.json`

```json
{
  "kind": "Analytic",
  "object": {
    "caption": "Warehouse", "caption_ru": "Склад",
    "description": "Reusable analytic over the Warehouse dictionary",
    "edtMetaId": "<GUID Ref-EDT на справочник>",
    "metaId": "<GUID-аналитики>", "name": "<Имя>",
    "modelId": "<GUID модели-владельца>", "layerId": 1
  }
}
```

Аналитика чужой модели переиспользуется привязкой — объяви зависимость
(`zuloone-new-model`).

## 2. Регистр — `Registers/<Имя>/<Имя>.object.json`

```json
{
  "kind": "Register",
  "object": {
    "caption": "<English caption>", "caption_ru": "<Русская подпись>",
    "registerEngineType": "Standard",
    "isDoubleEntry": false,
    "allowNegativeBalance": true,
    "useBalanceTable": true,
    "metaId": "<GUID-регистра>", "name": "<Имя>",
    "modelId": "<GUID модели>", "layerId": 1
  },
  "resources": [
    { "registerMetaId": "<GUID-регистра>", "fieldName": "Qty", "name": "Qty",
      "edtMetaId": "<GUID Qty-EDT>", "isOperational": true, "displayOrder": 1,
      "metaId": "<GUID>", "modelId": "<GUID модели>", "layerId": 1 }
  ],
  "analytics": [
    { "registerMetaId": "<GUID-регистра>", "analyticMetaId": "<GUID аналитики Warehouse>",
      "isRequired": true, "displayOrder": 1, "name": "<Имя>-Warehouse",
      "metaId": "<GUID>", "modelId": "<GUID модели>", "layerId": 1 },
    { "registerMetaId": "<GUID-регистра>", "analyticMetaId": "<GUID аналитики Item>",
      "isRequired": true, "displayOrder": 2, "name": "<Имя>-Item",
      "metaId": "<GUID>", "modelId": "<GUID модели>", "layerId": 1 }
  ]
}
```

Осознанные выборы:
- `isRequired: true` у привязки — движение без значения этой аналитики
  отклоняется; ключевые разрезы учёта делай обязательными.
- `allowNegativeBalance: false` — платформа не даст списать в минус.
- `isDoubleEntry` — когда true и когда false, см. ниже.
- Добавить разрез позже = дописать привязку в `analytics` — старые движения
  остаются валидными (у них этой аналитики просто нет).

### Когда регистру нужен `isDoubleEntry: true`, а когда нет

По умолчанию `false` — движение накапливается ОДНОСТОРОННЕ (`transactions.Add`):
приход/расход по одной комбинации аналитик (склад+товар, оборот по клиенту…).
Это подавляющее большинство регистров, включая складские и учёта оборотов —
списание при отгрузке ничем не отличается от прихода, просто с минусом.

Ставь `true` ТОЛЬКО когда движение — это ПЕРЕНОС ресурса между ДВУМЯ наборами
аналитик ВНУТРИ ОДНОГО И ТОГО ЖЕ регистра, и обе стороны обязаны провестись
атомарно, одной проводкой (`transactionPairs.Add(outcome, income)`), в сумме
давая ноль — иначе движение отклоняется целиком:
- перемещение между складами: `ИсходныйСклад -qty` / `ЦелевойСклад +qty`
  одной парой в регистре `Stock`;
- денежный регистр с двумя лицевыми счетами (дебет/кредит).

**НЕ ставь `true` только потому, что движение уменьшает остаток** (списание,
отгрузка, расход, амортизация) — это ОДНОСТОРОННЕЕ движение с отрицательным
`Qty`/`Amount`, регистр остаётся `false`. «Другая сторона» бизнес-операции
(выручка от продажи, себестоимость, обороты) почти всегда живёт в ДРУГОМ
регистре — это НЕ делает исходный регистр парным. Складской регистр,
проводящий и приход, и расход по ОДНОМУ и тому же складу — не двойная запись:
это накопление одного ресурса, `isDoubleEntry: false`.

Несоответствие (`isDoubleEntry: true` при скрипте, пишущем в `transactions`,
а не `transactionPairs`) платформа НЕ ловит на экспорте/импорте или
компиляции — она отклоняет проводку в момент реального ПРОВЕДЕНИЯ документа
(`DocumentPostingService`), с ошибкой вида `"Register '<Имя>' is
double-entry. Post to it via transactionPairs.Add(...)"`. Значит скрытая
ошибка флага всплывает только интеграционным тестом на проведение — держи
такой тест обязательным для каждого нового регистра (см. §6), не полагайся
на `zuloone-verify`-компиляцию.

**Живой пример, чем это кончается.** `Stock` пробовали сделать двойным: приход и
отгрузка проводились парами против sentinel-ячейки «внешний мир». От этого
отказались — остаток ячейки и есть фактическое наличие, а встречная нога только
запутывала отчёты. Регистр вернули в `isDoubleEntry: false`, но **скрипты
остались писать парами**, и каждое проведение любого документа стало падать с
«Register 'Stock' is not double-entry». Меняешь флаг — в том же заходе меняй ВСЕ
проводки в него и комментарии к ним, иначе разъедется молча до первого теста.

**Цена `allowNegativeBalance: true`.** Движок перестаёт проверять уход в минус
СОВСЕМ. Если остаток обязан быть неотрицательным (склад), защита переезжает в
`OnBeforePostAsync` документа и становится ЕДИНСТВЕННОЙ — не подстраховкой
поверх движковой. Заводи такую проверку сразу вместе с флагом.

## 3. Проводки по аналитикам

В транзакционных скриптах — `RegisterMovementSpec` (умный `.Dim` сам
маршрутизирует имя в аналитику, `.An` — явно):

```csharp
transactions.Add(new RegisterMovementSpec("<Имя>")
    .Dim("Warehouse", document.Warehouse)
    .Dim("Item", line.Item)
    .Res("Qty", line.Quantity ?? 0m));
```

В интеграционных тестах `Db.PostMovementAsync` маршрутизирует так же: имя,
не являющееся физическим измерением, уходит в привязанную аналитику.

## 4. Движок: стандартный или драйвер

Обычному складскому/денежному регистру хватает `registerEngineType: "Standard"` —
драйвер НЕ нужен. Драйвер итогов нужен для специальных алгоритмов расчёта
(FIFO-себестоимость, курсовые разницы):

`TotalDrivers/<Имя>/<Имя>.driver.json`:
```json
{
  "kind": "TotalDriver",
  "object": {
    "caption": "<Подпись>", "baseClassName": "FifoTotalDriver", "baseEngine": "Standard",
    "isKernel": false,
    "metaId": "<GUID-драйвера>", "name": "<Имя>",
    "modelId": "<GUID модели>", "layerId": 1
  }
}
```

- `baseClassName` — платформенный движок-предок: `DefaultTotalDriver` |
  `FifoTotalDriver` | `FifoStockTotalDriver` | `StockBaseTotalDriver` |
  `StockQtyBaseTotalDriver` | `MoneyTotalDriver` | `CashlessMoneyTotalDriver` |
  `MarginTotalDriver` | `CurrencyExchangeTotalDriver`. Их исходники — read-only
  `*.engine.cs` в `Core/TotalDrivers/`.
- Хук-скрипт драйвера (`<Имя>TotalDriver.*` рядом) платформа создаёт сама —
  partial БЕЗ базы, переопределяет только нужные хуки (`CalculateOutcomes`,
  `CalculatePartialAmount`…).
- Драйвер привязывается к регистру полем `totalDriverMetaId` в object.json
  регистра — его движок замещает собственный.

## 5. Пункт меню

Пункт `targetType: "Register"` в `Menu/menu.json`, `parentMetaId` — GUID
подгруппы **`Registers`/«Итоги»** модели (`zuloone-new-model`), не корневой
группы напрямую.

## 6. Проверка

Скилл `zuloone-verify` + интеграционный тест: `Db.PostMovementAsync("<Имя>",
дата, {"Warehouse": wh, "Item": item}, {"Qty": 5m})` и сверка
`QueryMovementsAsync`/`QueryBalancesAsync`; обязательная аналитика без
значения должна отклоняться; для драйвера — сценарий на его алгоритм.

## 7. Кросс-регистровые отчёты — виртуальный итог (Virtual Total)

Нужно смержить НЕСКОЛЬКО регистров в один отчёт (склад по Item+Cell и
себестоимость по Item под одной шапкой) — не пиши сервис-джойнер с нуля,
если хватает декларативного `VirtualTotal`: `dim`/`var` задают общую схему
отчёта, `group` мержит источники.

Файл — та же пара-конвенция, что `.script.json`+`.cs`: `<Имя>.json`
(`kind: "VirtualTotal"`, только caption/язык/metaId) + сосед `<Имя>.vt` —
сам текст DSL:

```
dim Item(Item): en(Item)
var Qty: en(Quantity)
var Value: en(Value)
predicate NamedWh(WAREHOUSE): TBWarehouseByName
group StockQty: en(Stock quantity)
  var Qty
    total Stock: Qty
end
group CostValue: en(Cost value)
  var Value
    total InventoryValue: Value
end
group All: en(Everything)
  children: StockQty, CostValue
end
```

Ключевое поведение (проверено в `VirtualTotalCompiler`/`VirtualTotalLanguage`):
- Компилируется в ОДИН `UNION ALL` по СЫРЫМ таблицам движений `TR_<Регистр>`
  источников — это движения, не остатки; баланс = `SUM(...)` по результату,
  как у любого регистра.
- Каждый `total <Регистр>: <Ресурс>` сам решает, какие СВОИ измерения
  замэппить в общую `dim`-схему (`dim Target: SourceDim`, или неявно — по
  совпадению имени). Разные наборы измерений у источников (у одного —
  физические Cell+Item, у другого — только динамическая аналитика Item) —
  НЕ проблема: несовпавшее измерение просто NULL в этой строке.
- **Это UNION, а не JOIN.** Строка результата приходит РОВНО из одного
  источника и несёт значение только СВОЕЙ переменной — остальные `var` в
  этой строке нулевые. `Qty`-из-Stock и `Value`-из-InventoryValue лягут в
  РАЗНЫЕ строки одной группы, не в одну колонку рядом. Чтобы получить цену
  (`Value/Qty`) в ОДНОЙ строке отчёта — после `QueryVirtualTotalAsync` нужен
  ещё `GROUP BY dim, SUM(Qty), SUM(Value)` и деление в коде (сервис/отчёт);
  сам DSL арифметики между переменными не считает (только знак и
  `.Add/.Sub/.Debit/.Credit` фильтр значения).
- `predicate Имя(Аргументы): СистемноеИмя` — именованный переиспользуемый
  SQL (`kind: "VirtualTotalPredicate"`, поля `sqlQuery`+`arguments`), в DSL
  — `filter [not] Имя(измерения)` → `EXISTS`/`NOT EXISTS` по этому SQL.
- `group`-ы регенерируются из DSL при каждой компиляции
  (`MetaVirtualTotalGroup`), иерархия — через `children: A, B`; руками
  строки групп не редактировать.
- Тестовый доступ: `Db.CompileVirtualTotalAsync("Имя")` (`.Success`,
  `.Errors`, `.GroupCount`) и `Db.QueryVirtualTotalAsync("Имя")` → строки со
  служебными `GroupMetaId`/`MovementDate`/`DocumentMetaId` + колонки
  dim/var.

**Не путать с `registerEngineType: "Virtual"`** (тот же корень слова, другой
механизм) — это ОДИН регистр БЕЗ физической таблицы, чей остаток целиком
считает скрипт-драйвер (`VirtualTotalScriptBase.GetBalancesAsync`,
кернел-драйвер `Core/TotalDrivers/VirtualTotalDriver`), а не декларативный
мердж НЕСКОЛЬКИХ регистров в отчёт.
