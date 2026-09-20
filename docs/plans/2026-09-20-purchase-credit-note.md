# Кредит-нота закупки — план реализации

**Цель:** сторно входного НДС отдельным документом `PurchaseCreditNote` против оприходованного заказа; заказ остаётся Received; возврат по-прежнему только нетто и склад.
**Подход:** зеркало `SalesCreditNote`. Нота пишет `Payable −НДС`. After-post — `CreateReversalAsync("INPUT")`. GL `TaxCalculation` получает минус-ветку входного НДС; шов `Payable` на минусе не пишет.
**ТЗ:** `d:\Sources\zulo.one\docs\superpowers\specs\2026-09-20-purchase-credit-note-design.md`

## Модели и слои

| Объект | Модель-владелец | Слой | Нужна зависимость |
|---|---|---|---|
| PurchaseCreditNote, команда, tx, печать, тест | Purchasing | Solution(2) | Tax, Inventory, Organization, Common, Accounting — уже есть |
| TaxCalculationGLEventHandler минус INPUT | GLIntegration | Solution(2) | Tax, Purchasing — уже есть |

## metaId, выданные планом

| Объект | metaId |
|---|---|
| PurchaseCreditNoteSeq | `62d14954-2ad5-49dc-881d-406be34dd153` |
| PurchaseCreditNoteLines (тип ТЧ) | `2c0f0264-285a-4b8c-8ad9-823812826b22` |
| Lines.Item | `b42ba570-d3e9-43ee-88f6-89fbcb38e334` |
| Lines.Quantity | `2ae7a8ab-eb69-4c6f-b6ad-18b85ec3365f` |
| Lines.UnitPrice | `72122ba6-be7b-4ae4-a946-6dbc9f48e112` |
| PurchaseCreditNoteLinesEventHandler | `d490239c-d0de-404a-baea-e2936cdeceb5` |
| PurchaseCreditNote | `10f6b734-1288-40f2-aa0f-3717a4e3995f` |
| Header.ID | `57b75d6c-70e1-44b4-ae40-8b9c8b3aa564` |
| Header.Supplier | `16a0e8fb-c113-4a26-92ee-6cba34dd9094` |
| Header.OriginalOrder | `269f2a3c-e91f-4b26-ac62-51bb41c48caf` |
| Header.LegalEntity | `e900db07-20b0-4307-8d92-d83b68a3778b` |
| Header.TaxRateApplied | `e67b7e05-e5b8-4479-9e31-bdfb2722b771` |
| Subtype Draft | `3de064b4-3dd0-441b-87d2-18de29ee893b` |
| Subtype Posted | `c817dae1-d658-4f61-928e-d327bd3b8a08` |
| tableParts Lines bind | `df7af698-496b-4dc2-984c-959cebd5b3c3` |
| subtypeTransactionScripts bind | `ec9d4406-33a9-4d68-9cf5-7d01dad220ed` |
| Draft→Posted transition | `c9102767-4dc3-42a0-ae22-14c0d0289edf` |
| PurchaseCreditNoteEventHandler | `7f9c35de-e226-4589-8259-01450ba2e063` |
| PurchaseCreditNotePayableTx | `0e69df19-07ad-4e5f-a085-b64a713de92f` |
| PostPurchaseCreditNote | `5cf05ecb-fa45-4dde-8044-177542fb8c81` |
| PostPurchaseCreditNoteScript | `5389fce1-0a3f-478c-b240-60fad89e9865` |
| command subtypeBinding | `aa89a9cd-6238-4637-8379-b7315518cdf2` |
| Menu PurchaseCreditNote | `13a8bc2e-fdba-4d81-b94d-0bd2b5b65300` |
| PurchaseCreditNoteTest | `cc4ed6be-c3f9-4d8a-aa34-efc1ebee7400` |
| PurchaseCreditNoteXr | `2b292a6b-3c0e-4718-a971-98672fe313ab` |
| PurchaseCreditNoteXrPrintForm | `3f110aaf-c012-4087-a0d3-d18afd9029f7` |
| PurchaseCreditNoteXrPrintTest | `beb38f4b-7d60-4359-9b05-1356e8b96845` |

Существующие EDT (не создавать): Supplier `a0362f8d-88dc-4b26-bec9-cf9847d074bb`, LegalEntity `4ddbfca6-2464-452f-b913-443463c0094f`, TaxRateApplied `713835c7-1701-4c43-9cf7-c754519dc5d4`, Item `5cea90d4-69cc-4e90-8dd1-fc57eef4742a`, Quantity `7c8bdcd2-b2d6-433e-8fb3-3d182a561200`, UnitPrice `8aaf0600-87ce-4f9d-b103-cc1379103167`, Item dict `319da250-b783-4c33-aa69-1c6ced3be605`. Меню Documents `c946d33a-7f47-4846-9563-d42b9a6ba275`. Purchasing `048b6f73-1c02-4ad7-857c-e19a3b5056c3`. GLIntegration `3d7f0a94-6c2b-4e8d-9a5f-1b4c7e0d2a80`. InputVatGLTest `b2d8f04e-6c19-4a53-8e71-1f9a5c3d0b47`.

## Строковые имена

| Что | Точное значение |
|---|---|
| Документ | `PurchaseCreditNote` |
| ТЧ | `Lines` / тип `PurchaseCreditNoteLines` |
| Подтипы | `Draft` → `Posted` |
| Команда | `PostPurchaseCreditNote` |
| Регистр | `Payable` |
| Аналитика | `Supplier` |
| Ресурс | `Amount` |
| Направление расчёта | `INPUT` |
| Reason расчёта | `Purchase credit note {note.MetaId:D}` |
| Печатная форма | `PurchaseCreditNoteXr` |
| Классы | `PurchaseCreditNoteEventHandler`, `PurchaseCreditNoteLinesEventHandler`, `PurchaseCreditNotePayableTx`, `PostPurchaseCreditNoteCommand`, `PurchaseCreditNoteXrPrintForm`, `PurchaseCreditNoteTest`, `PurchaseCreditNoteXrPrintTest` |

## Общие требования

- `modelVersion`: Purchasing `1.5.0` → `1.6.0` (задача 1), затем `1.7.0` с печатью (задача 4). GLIntegration `1.1.14` → `1.1.15` (задача 3).
- Целое — `Integer`, не `Int`. Секретов нет. Скрипты без HttpClient/FS.
- Apply: `POST /api/dev/workspace/apply-file?force=true` на `.script.json` (sidecar увезёт `.cs`). Не `sync-resume`.
- Новый документ после `schema/sync` не получает сущность, пока не `docker restart zuloone-core-1`.
- Компиляция: все модели `Ok`. Тест: `outcome` = `Passed`/`Failed`.
- Коммит: обе половины каждой пары скрипта.

---

### Задача 1: документ, команда, tx, события, меню

**Файлы:** создать все ниже; изменить `Purchasing/Menu/menu.json`, `Purchasing/model.json`.

**Контракт:**
- Потребляет: EDT из шапки, регистр `Payable`, `ITaxService`, `IStoreCellService`, `IPricingService`
- Отдаёт: документ `PurchaseCreditNote`, команда `PostPurchaseCreditNote`, класс `PurchaseCreditNotePayableTx`

- [ ] **Шаг 1. Написать файлы**

`Purchasing/NumberSequences/PurchaseCreditNoteSeq.json`:

```json
{
  "kind": "NumberSequence",
  "object": {
    "padLength": 0,
    "startValue": 1000,
    "increment": 1,
    "resetPolicy": "None",
    "metaId": "62d14954-2ad5-49dc-881d-406be34dd153",
    "name": "PurchaseCreditNoteSeq",
    "modelId": "048b6f73-1c02-4ad7-857c-e19a3b5056c3"
  }
}
```

`Purchasing/TableParts/PurchaseCreditNoteLines.json` — как `PurchaseReturnLines.json`, имена/metaId из таблицы шапки, `tableName`: `TP_PurchaseCreditNoteLines`, `className`: `PurchaseCreditNoteLinesTablePartRow`.

`Purchasing/TableParts/PurchaseCreditNoteLines/Events/PurchaseCreditNoteLinesEventHandler.script.json`:

```json
{
  "kind": "Script",
  "object": {
    "scriptType": "EventHandler",
    "objectType": "TablePart",
    "objectName": "PurchaseCreditNoteLines",
    "objectMetaId": "2c0f0264-285a-4b8c-8ad9-823812826b22",
    "executionOrder": 0,
    "metaId": "d490239c-d0de-404a-baea-e2936cdeceb5",
    "name": "PurchaseCreditNoteLinesEventHandler",
    "modelId": "048b6f73-1c02-4ad7-857c-e19a3b5056c3"
  }
}
```

`Purchasing/TableParts/PurchaseCreditNoteLines/Events/PurchaseCreditNoteLinesEventHandler.cs`:

```csharp
#nullable enable
namespace ZuloOne.Runtime.Generated;

public partial class PurchaseCreditNoteLinesEventHandler : TypedTablePartEventHandler<PurchaseCreditNoteLinesTablePartRow>
{
}
```

`Purchasing/Documents/PurchaseCreditNote/PurchaseCreditNote.object.json` — структура как `PurchaseReturn.object.json`:

- caption en `Purchase credit note`, ar `إشعار خصم المشتريات`, ru `Кредит-нота закупки`, uk `Кредит-нота закупівлі`
- description: `Сторно входного НДС и налоговой части кредиторки по оприходованному заказу. Заказ не Voided. Склад и нетто — у возврата.`
- `numberSequenceMetaId` = seq, `requiresPosting` true, `postOnSave` true, icon `fluent-color:document-arrow-left-24`
- header: ID (system string 50, как у возврата), Supplier edt required displayOrder 1, OriginalOrder Guid displayOrder 2, LegalEntity edt displayOrder 3 caption en `Buying legal entity` / ru `Юрлицо покупателя`, TaxRateApplied edt displayOrder 4
- subtypes Draft (initial, displayOrder 1) / Posted (read-only, displayOrder 2)
- tableParts Lines → тип ТЧ
- subtypeTransactionScripts Posted → `0e69df19-…` executionOrder 1
- subtypeTransitions Draft→Posted

`Purchasing/Documents/PurchaseCreditNote/Events/PurchaseCreditNoteEventHandler.script.json` — EventHandler Document, objectMetaId документа, name `PurchaseCreditNoteEventHandler`, metaId `7f9c35de-…`.

`Purchasing/Documents/PurchaseCreditNote/Events/PurchaseCreditNoteEventHandler.cs`:

```csharp
#nullable enable
using System.Collections.Generic;
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

namespace ZuloOne.Runtime.Generated;

public partial class PurchaseCreditNoteEventHandler : TypedDocumentEventHandler<PurchaseCreditNote>
{
    public override async Task<EventResult> OnBeforeSaveAsync(PurchaseCreditNote header, bool isNew, EventContext context)
    {
        var prior = await next(header, isNew, context);
        if (!prior.Success) return prior;

        if (header.OriginalOrder == Guid.Empty)
            return EventResult.Ok();

        var order = await context.GetService<IDocumentManager>()
            .GetDocumentAsync<PurchaseOrder>(header.OriginalOrder);
        if (order is null)
            return EventResult.Ok();

        if (header.Supplier == Guid.Empty)
            header.Supplier = order.Supplier;
        if (header.LegalEntity == Guid.Empty)
        {
            var le = await context.GetService<IStoreCellService>().GetLegalEntityAsync(order.Location);
            if (le is Guid id)
                header.LegalEntity = id;
        }
        if (header.TaxRateApplied == 0m)
        {
            var tax = context.GetService<ITaxService>();
            var code = await tax.ResolveDefaultTaxCodeAsync();
            if (code is Guid taxCode)
            {
                var rate = await tax.ResolveRateAsync(taxCode, TaxPointOf(order));
                if (rate is decimal r)
                    header.TaxRateApplied = r;
            }
        }

        if (header.Lines.Count == 0)
        {
            foreach (var line in order.Lines)
            {
                header.Lines.Add(new PurchaseCreditNoteLinesTablePartRow
                {
                    Item = line.Item,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                });
            }
        }

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnBeforePostAsync(PurchaseCreditNote document, EventContext context)
    {
        var prior = await next(document, context);
        if (!prior.Success) return prior;
        if (document.Subtype != PurchaseCreditNote.Subtypes.Posted)
            return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<PurchaseCreditNote>(document.MetaId) ?? document;
        if (full.OriginalOrder == Guid.Empty)
            return EventResult.Cancel("Укажите исходный заказ");

        var order = await docs.GetDocumentAsync<PurchaseOrder>(full.OriginalOrder);
        if (order is null || order.Subtype != PurchaseOrder.Subtypes.Received)
            return EventResult.Cancel("Кредит-нота только по оприходованному заказу");

        if (full.Supplier == Guid.Empty)
            return EventResult.Cancel("Укажите поставщика");
        if (full.Supplier != order.Supplier)
            return EventResult.Cancel("Поставщик ноты должен совпадать с заказом");
        if (full.LegalEntity == Guid.Empty)
            return EventResult.Cancel("Не определено юрлицо покупателя — кредит-нота не проводится");

        var lines = full.Lines;
        if (lines.Count == 0)
            return EventResult.Cancel("Заполните строки кредит-ноты");
        if (lines.Any(l => l.Quantity <= 0m))
            return EventResult.Cancel("Количество в строке должно быть больше нуля");

        var rate = full.TaxRateApplied != 0m ? full.TaxRateApplied : 0m;
        if (rate <= 0m)
            return EventResult.Cancel("На приходе нет НДС — кредит-ноту выставлять нечего");

        return EventResult.Ok();
    }

    public override async Task<EventResult> OnAfterPostAsync(PurchaseCreditNote header, EventContext context)
    {
        var prior = await next(header, context);
        if (!prior.Success) return prior;
        if (header.Subtype != PurchaseCreditNote.Subtypes.Posted)
            return EventResult.Ok();

        var docs = context.GetService<IDocumentManager>();
        var note = await docs.GetDocumentAsync<PurchaseCreditNote>(header.MetaId);
        if (note is null || note.Lines.Count == 0) return EventResult.Ok();

        var legalEntity = note.LegalEntity;
        if (legalEntity == Guid.Empty) return EventResult.Ok();

        var pricing = context.GetService<IPricingService>();
        var taxBase = note.Lines.Sum(l => pricing.LineAmount(l.Quantity, l.UnitPrice));
        var taxPoint = note.DocumentDate == default ? DateTime.UtcNow.Date : note.DocumentDate.Date;
        var calc = await context.GetService<ITaxService>()
            .CreateReversalAsync(legalEntity, "INPUT", taxBase,
                $"Purchase credit note {header.MetaId:D}", taxPoint,
                new Dictionary<string, object?>
                {
                    ["document.type"] = "PurchaseCreditNote",
                    ["direction"] = "INPUT",
                    ["amount"] = taxBase,
                });
        if (calc.HasValue)
            await docs.AddLinkAsync(header.MetaId, calc.Value);

        return EventResult.Ok();
    }

    private static DateTime TaxPointOf(PurchaseOrder document)
        => document.DocumentDate == default ? DateTime.UtcNow.Date : document.DocumentDate.Date;
}
```

Владелец событий: `next` НЕ звать.

`Purchasing/Documents/PurchaseCreditNote/Transactions/PurchaseCreditNotePayableTx.script.json`:

```json
{
  "kind": "Script",
  "object": {
    "scriptType": "TransactionScript",
    "objectType": "Document",
    "objectName": "PurchaseCreditNote",
    "objectMetaId": "10f6b734-1288-40f2-aa0f-3717a4e3995f",
    "executionOrder": 1,
    "metaId": "0e69df19-07ad-4e5f-a085-b64a713de92f",
    "name": "PurchaseCreditNotePayableTx",
    "modelId": "048b6f73-1c02-4ad7-857c-e19a3b5056c3"
  }
}
```

`Purchasing/Documents/PurchaseCreditNote/Transactions/PurchaseCreditNotePayableTx.cs`:

```csharp
#nullable enable
using ZuloOne.Services.Contracts;

public partial class PurchaseCreditNotePayableTx
{
    protected override void GetTransactions(PurchaseCreditNote document, TransactionPairCollection transactionPairs, TransactionCollection transactions)
    {
        var rate = document.TaxRateApplied;
        if (rate <= 0m) return;

        var pricing = GetService<IPricingService>();
        var tax = GetService<ITaxService>();
        decimal baseAmount = 0m;
        foreach (var line in document.Lines)
            baseAmount += pricing.LineAmount(line.Quantity, line.UnitPrice);

        var vat = tax.CalculateTax(baseAmount, rate);
        if (vat == 0m) return;

        transactions.Add(new RegisterMovementSpec("Payable")
            .An(Analytics.Payable.Supplier, document.Supplier)
            .Res("Amount", -vat));
    }
}
```

Команда — тройка как `PostPurchaseReturn`:

`PostPurchaseCreditNote.json`: caption en `Post purchase credit note` / ru `Провести кредит-ноту закупки`; `scriptMetaId` = `5389fce1-…`; `metaId` = `5cf05ecb-…`; binding на Draft `3de064b4-…`.

`PostPurchaseCreditNoteScript.script.json`: objectType Command, objectMetaId команды, name `PostPurchaseCreditNoteScript`.

`PostPurchaseCreditNoteScript.cs`:

```csharp
using System.Linq;
using ZuloOne.Managers;
using ZuloOne.Services.Contracts;

public partial class PostPurchaseCreditNoteCommand
{
    public override async Task ExecuteAsync(PurchaseCreditNote document, CommandContext context)
    {
        var docs = context.GetService<IDocumentManager>();
        var full = await docs.GetDocumentAsync<PurchaseCreditNote>(document.MetaId);
        if (full == null) return;

        if (full.OriginalOrder == Guid.Empty)
        {
            context.AddClientAction(ClientAction.Message("Укажите исходный заказ."));
            return;
        }
        if (full.Lines.Count == 0)
        {
            context.AddClientAction(ClientAction.Message("Нельзя провести пустую кредит-ноту: добавьте строки."));
            return;
        }
        if (full.Lines.Any(l => l.Quantity <= 0m))
        {
            context.AddClientAction(ClientAction.Message("Количество в строке должно быть больше нуля."));
            return;
        }

        full.Subtype = PurchaseCreditNote.Subtypes.Posted;
        await docs.SaveDocumentAsync(full);
        context.AddClientAction(ClientAction.Message("Кредит-нота закупки проведена."));
    }
}
```

Меню: пункт после `PurchaseReturn`, `displayOrder` 3, `parentMetaId` Documents, `targetType` Document, `targetMetaId` `10f6b734-…`, name `PurchaseCreditNote`, caption en `Purchase credit notes` / ru `Кредит-ноты закупки`.

`Purchasing/model.json`: `modelVersion` `"1.6.0"`.

- [ ] **Шаг 2. Синк применил** — apply-file force каждого json (скрипты — `.script.json`). Лог: `applied`, не warning.

- [ ] **Шаг 3. Компиляция** `POST http://localhost:5257/api/metadata/models/compile` — Purchasing `Ok`.

- [ ] **Шаг 4. Схема** `POST http://localhost:5257/api/schema/sync`.

- [ ] **Шаг 5. Перезапуск** `docker restart zuloone-core-1`, дождаться `/health`. Без этого `PurchaseCreditNote` — CS0246.

- [ ] **Шаг 6. Бамп** уже в шаге 1 (`1.6.0`).

- [ ] **Шаг 7. Коммит** обе половины всех скриптов + json.

```
git add Purchasing/NumberSequences/PurchaseCreditNoteSeq.json \
        Purchasing/TableParts/PurchaseCreditNoteLines.json \
        Purchasing/TableParts/PurchaseCreditNoteLines/Events \
        Purchasing/Documents/PurchaseCreditNote \
        Purchasing/Commands/Document/PostPurchaseCreditNote \
        Purchasing/Menu/menu.json Purchasing/model.json
git commit -m "feat(purchasing): PurchaseCreditNote reverses input VAT, not the order"
```

---

### Задача 2: `PurchaseCreditNoteTest`

**Файлы:**
- Создать ПАРОЙ: `Purchasing/Tests/PurchaseCreditNoteTest.json` + `PurchaseCreditNoteTest.cs`
- `modelVersion` остаётся `1.6.0` (тест не объект поставки тенанту, но пара в git обязательна)

**Контракт:** строковые `CreateDocumentAsync` не нужны — тип уже есть после задачи 1. Фикстура как `PurchaseInputTaxTest` (ячейка приёмки, 15% INPUT). Приход: `Draft → Ordered → Received` через `SaveDocumentAsync` (маршрут, не прыжок). Ноту проводить командой `PostPurchaseCreditNote`.

`PurchaseCreditNoteTest.json`:

```json
{
  "kind": "Test",
  "object": {
    "description": "Кредит-нота закупки сторнирует только входной НДС: заказ остаётся Received, частичные и сверхзаказные строки законны, возврат+нота гасят гросс",
    "isAutoGenerated": false,
    "isActive": true,
    "groupName": "Бизнес-слой.Продажи и расчёты",
    "isExtension": false,
    "metaId": "cc4ed6be-c3f9-4d8a-aa34-efc1ebee7400",
    "name": "PurchaseCreditNoteTest",
    "modelId": "048b6f73-1c02-4ad7-857c-e19a3b5056c3"
  }
}
```

`PurchaseCreditNoteTest.cs` — класс `PurchaseCreditNoteTest : IntegrationTestScriptBase`. usings: `System`, `System.Collections.Generic`, `System.Linq`, `System.Threading.Tasks`, `ZuloOne.Runtime.Testing`, `ZuloOne.Managers`, `ZuloOne.Runtime.Generated`, `ZuloOne.Services.Contracts`.

Setup: валюта/страна/юрлицо/подразделение/магазин/зона/ячейка Receiving/`Item`/`Supplier` как в `PurchaseInputTaxTest`. `ConfigureTaxAsync` — INPUT+OUTPUT, ставка 0.15 с 2020-01-01, DefaultTaxCode. Extra item для сверхзаказной строки.

Хелперы:
- `ReceiveAsync(Setup, qty, price)` — NewDocument PurchaseOrder, Location, Supplier, line, Save, Ordered, Save, Received, Save, вернуть перечитанный.
- `PayableAsync(Setup)` — `TotalsManager.GetBalanceAsync("Payable", "Amount", Supplier)`.
- `PostNoteAsync` — `FindCommandIdAsync("document", "PostPurchaseCreditNote")` + `ExecuteDocumentCommandAsync`.
- `LinkedCalcAsync(noteId)` — family edges, первый `TaxCalculation`.

Кейсы (имена assert как у продажи):

1. `CreditReversesVatKeepsOrderReceived` — приход 4×25, долг 115; save ноты копирует строки и ставку; команда; заказ Received; Payable 100; calc TaxBase −100, TaxAmount −15.
2. `PartialCreditUsesOwnLines` — свои 2×25 до save; Payable 107.5; calc −7.5.
3. `ExtraLineBeyondOrderAllowed` — 4×25 + extra 1×25; Payable 96.25; TaxBase −125.
4. `RefusesUnorderedOrder` — нота на Draft-заказ, Subtype=Posted Save бросает, сообщение содержит `оприходованному`.
5. `RefusesWhenReceiptHasNoTax` — без контура (гасить DefaultTaxCode); отказ содержит `НДС`.
6. `NoteWithoutReturnLeavesNetPayable` — только нота, Payable = нетто (100), склад не падает.
7. `ReturnThenCreditClearsGross` — возврат Posted (Location+Supplier+OriginalOrder+строки), Payable 15, затем нота → 0; заказ Received.

Команда без строк оставляет Draft — отдельный кейс не обязателен, покрыто OnBeforePost.

Прогнать `POST /api/metadata/tests/cc4ed6be-c3f9-4d8a-aa34-efc1ebee7400/run`. Снять отказ «оприходованному» один раз — тест обязан покраснеть — вернуть.

Коммит пары теста.

---

### Задача 3: GL минус INPUT

**Файлы:**
- Изменить: `GLIntegration/DocumentExtensions/TaxCalculation.GLIntegration/TaxCalculationGLEventHandler.cs`
- Изменить: `GLIntegration/Tests/InputVatGLTest.cs` (+ description в json)
- Изменить: `GLIntegration/model.json` → `1.1.15`

**Контракт:** шов `CloseReceivablePayableSeamAsync` остаётся `if (input > 0m)`. Журнал — `input != 0m`.

Заменить блок `if (input > 0m && !string.IsNullOrWhiteSpace(settings.VatReceivableAccountCode) && !string.IsNullOrWhiteSpace(settings.PayableAccountCode))` на:

```csharp
        if (input != 0m
            && !string.IsNullOrWhiteSpace(settings.VatReceivableAccountCode)
            && !string.IsNullOrWhiteSpace(settings.PayableAccountCode))
        {
            var amount = input > 0m ? input : -input;
            var debit = input > 0m ? settings.VatReceivableAccountCode : settings.PayableAccountCode;
            var credit = input > 0m ? settings.PayableAccountCode : settings.VatReceivableAccountCode;
            var jeId = await gl.PostAsync(
                calc.DocumentDate, le.MetaId, le.Currency, amount,
                debit, credit,
                (input > 0m ? "Input VAT " : "Input VAT reversal ") + header.MetaId,
                input > 0m ? "НДС к возмещению" : "Кредиторка (сторно налога)",
                input > 0m ? "Кредиторка (налог поставщику)" : "НДС к возмещению",
                TaxCircuits);
            if (jeId.HasValue) posted.Add(jeId.Value);
        }
```

В `InputVatGLTest` добавить кейс `InputVatReversalCreditsAsset` после прихода 10×3: создать `PurchaseCreditNote` с `OriginalOrder`, Save, команда `PostPurchaseCreditNote`, взять calc ноты, `AccountAsync(calc, vat)` Credit == 4.5, `AccountAsync(calc, payable)` Debit == 4.5. Существующие кейсы не ломать. Description json дописать про сторно.

Apply `.script.json` хендлера (sidecar `.cs`). Compile GLIntegration Ok. Прогон `b2d8f04e-6c19-4a53-8e71-1f9a5c3d0b47`.

Коммит: handler pair + test + model.json.

---

### Задача 4: печать

**Файлы:**
- `Purchasing/Documents/PurchaseCreditNote/PrintForms/PurchaseCreditNoteXr.printform.json`
- `PurchaseCreditNoteXrPrintForm.script.json` + `.cs`
- `Purchasing/Tests/PurchaseCreditNoteXrPrintTest.json` + `.cs`
- `Purchasing/model.json` → `1.7.0`

Печатная форма: `documentTypeMetaId` = документ, `scriptMetaId` = `3f110aaf-…`, `engine` `Xr`, name `PurchaseCreditNoteXr`. `layoutJson` скопировать с `CreditNoteXr.printform.json`, заменить: title `PURCHASE CREDIT NOTE`; поле Customer → Supplier (binding `Supplier`); убрать Contract и Outlet; Seller оставить (юрлицо); `Original invoice` → `Original order`. Footer Subtotal/VAT/Total как у продажи.

`PurchaseCreditNoteXrPrintForm.cs` — как `CreditNoteXrPrintForm.cs`, но:
- `GetDocumentAsync<PurchaseCreditNote>`
- **не** звать `IEInvoiceRelease` / `BuyerReleaseBlockAsync`
- `NameAsync(..., "Supplier", doc.Supplier)`, `LegalEntity`, `PurchaseOrder` для `OriginalOrder`
- Row: Customer="", Supplier=имя, Seller=юрлицо, Notes=номер заказа
- `NameAsync` принимает `Guid?` (`id is not Guid g || g == Guid.Empty`)

`PurchaseCreditNoteXrPrintTest`: GetScriptAsync `3f110aaf-…`, asserts: `GetDocumentAsync<PurchaseCreditNote>`, `FormatAsync`, `IPricingService`, `OriginalOrder`, `ITaxService` или `TaxRateApplied`, **нет** `BuyerReleaseBlockAsync`; GetScriptsByObjectAsync Document `10f6b734-…` содержит скрипт. groupName `Бизнес-слой.Продажи и расчёты`, metaId `beb38f4b-…`.

Прогон print-теста. Коммит пары формы + теста + bump.

---

### Задача 5: вики

В `d:\Sources\zulo.one` (не воркспейс):

- `wiki/business/sales-and-purchasing.md`: в схему закупки после PurchaseReturn добавить блок `Draft → Posted PurchaseCreditNote` / `Payable− налог` / `TaxCalculation INPUT reverse` / `GL Dr кредиторка / Cr НДС к возмещению`. Убрать пункт «Сторно входного НДС» из «Чего нет». В таблицу проверок — `PurchaseCreditNoteTest`, `InputVatGLTest` reversal, `PurchaseCreditNoteXrPrintTest`.
- `wiki/business/process-status.md`: §8 leftover только «Счёт от поставщика отдельно от заказа». Пункт очереди 10 пометить **сделано 2026-09-20** (`PurchaseCreditNote`).
- `wiki/business/document-commands.md`: строка `PurchaseCreditNote` / `PostPurchaseCreditNote` / `Draft → Posted` / сторно входного НДС, заказ остаётся Received. У `PurchaseReturn` уточнение оставить.
- `wiki/business/settlements-and-ledger.md`: рядом с продажной кредит-нотой — входной НДС снимает `PurchaseCreditNote`, заказ Received.
- `wiki/business/product-direction.md`: у закупок «сторно входного НДС» больше не дыра; остаётся счёт от поставщика.
- `wiki/user/purchase-credit-note.md` (новый, коротко): возврат = товар и нетто; кредит-нота = НДС; заказ не сторнируется. Ссылка из `wiki/user/README.md`.

Коммит в репозитории `zulo.one`.

---

## Самопроверка плана

1. Спека: объект, штамп, отказы, Payable−VAT, CreateReversalAsync INPUT, GL минус, шов не на минусе, печать без IEInvoiceRelease, тесты, вики, out of scope — у каждой есть задача.
2. Нет «аналогично», TBD, «добавить валидацию».
3. metaId внутренние ссылки = таблица шапки.
4. Строки `Payable`/`Supplier`/`INPUT`/`Posted` совпадают.
5. Бамп Purchasing 1.6.0 и 1.7.0, GLIntegration 1.1.15.
6. Зависимости уже в model.json.
7. Имена классов уникальны.
8. Пары `.script.json` + `.cs` в коммит-шагах.
