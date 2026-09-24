---
name: zuloone-new-command
description: Создать команду документа — проверки и управляемый переход между подтипами (состояниями). Use when a subtype needs a guarded transition (validate, then move to the next state) rather than a bare SaveDocumentAsync.
---

# Новая команда

**Команда документа — единственная пользовательская точка входа в смену
подтипа.** После сохранения карточка не даёт выбрать подтип вручную:
кнопка команды проверяет условия и ставит целевой `Subtype`; движок
**заменяет** движения (семантика Mix): снимает проводки текущего
состояния и исполняет транзакционные скрипты **только целевого**
(проводки, остатки, книга). Ранние проводки не копятся — если они
нужны и после перехода, их скрипты должны висеть и на целевом подтипе.
Один скрипт можно повесить на несколько подтипов: позднее состояние
тогда **пересчитывает** те же проводки, а не хранит слой истории.
Нет привязки на целевом — строки регистра удаляются.
Без такой команды документ из UI навсегда черновик, сколько бы скриптов
ни висело на «Подтверждено».

Типичный контур: на `Draft` кнопка «Подтвердить» / «Провести» / «Оплатить» —
проверки (есть строки, хватает остатка…), затем
`document.Subtype = <Документ>.Subtypes.Posted` и `SaveDocumentAsync`.
Строковый литерал (`"Posted"`) не писать — подтип только из `Subtypes`.
Прямой `document.Subtype = <Документ>.Subtypes.…` из сервиса или теста
(см. `zuloone-new-document` §5) — для системных переводов, не вместо кнопки.

Команда — НЕ замена событиям перехода (`OnBeforePostAsync` и т.д. в
`zuloone-new-document` §4): события реагируют на ЛЮБОЙ переход (включая
API/интеграции), команда — это то, что нажимает пользователь.

Есть и команды без смены подтипа (подставить цены, развернуть BOM) — это
вспомогательные действия, они не проводят документ.

## Файловая тройка — `Commands/Document/<Имя>/`

Три файла в одной папке, имя папки = имя команды:

**1. `<Имя>.json`** — сама команда (envelope `DocumentCommand`) плюс
привязки к подтипам:

```json
{
  "kind": "DocumentCommand",
  "object": {
    "caption": { "en": "<English caption>", "ru": "<Русская подпись>" },
    "scriptMetaId": "<GUID скрипта — см. файл 2>",
    "parameterMode": "None",
    "displayOrder": 0,
    "beginGroup": false,
    "isEnabled": true,
    "requiresConfirmation": false,
    "reloadAfterExecution": true,
    "metaId": "<GUID-команды>",
    "name": "<Имя>",
    "modelId": "<GUID модели>" },
  "subtypeBindings": [
    { "metaId": "<GUID-привязки>", "documentCommandMetaId": "<GUID-команды>",
      "documentSubtypeMetaId": "<GUID подтипа, из которого доступна команда>" }
  ]
}
```

Пустой `subtypeBindings: []` = команда доступна на ЛЮБОМ подтипе документа —
для guarded-переходов почти всегда нужна ровно одна привязка (подтип-источник).

**2. `<Имя>Script.script.json`** — привязка кода к команде:

```json
{
  "kind": "Script",
  "object": {
    "scriptType": "DocumentCommand", "objectType": "Command",
    "objectMetaId": "<GUID-команды, тот же что metaId выше>",
    "objectName": "<Имя>",
    "executionOrder": 0,
    "metaId": "<GUID-скрипта, тот же что scriptMetaId выше>",
    "name": "<Имя>Script",
    "modelId": "<GUID модели>" }
}
```

**3. `<Имя>Script.cs`** — код, база генерится платформой
(`DocumentCommandBase<<Документ>>`, см. `.generated/Frameworks/`):

```csharp
public partial class <Имя>Command
{
    public override async Task ExecuteAsync(<Документ> document, CommandContext context)
    {
        var svc = context.GetService<I<Сервис>>();
        var error = await svc.Validate...Async(document.MetaId);
        if (error != null) { CreateUserMessage(error); return; }

        document.Subtype = <Документ>.Subtypes.<ЦелевойПодтип>;
        await DocumentManager.SaveDocumentAsync(document);
    }
}
```

Проверяй сначала, переходи — только если проверка прошла. Не проходит —
`CreateUserMessage("причина")` и `return` без изменения подтипа: пользователь
видит сообщение, документ остаётся на месте.

Чужую команду не копируй второй «рядом». Обёртка —
«Добавить свой поверх» на полоске этой команды, `nextAsync<CommandResult>`
(`zuloone-extend` §2г). Забытый `next` — ZOCOC003.

## Команда СПРАВОЧНИКА — `Commands/Dictionary/<Имя>/`

Та же тройка, три отличия. Подтипов у справочника нет, поэтому нет и
`subtypeBindings`; вместо них — `dictionaryMetaId` на самом объекте:

```json
{ "kind": "DictionaryCommand",
  "object": {
    "dictionaryMetaId": "<GUID справочника>",
    "caption": { "en": …, "ru": … },
    "scriptMetaId": "<GUID скрипта>", "parameterMode": "None",
    "reloadAfterExecution": true,
    "metaId": "<GUID-команды>", "name": "<Имя>", "modelId": "<GUID модели>" } }
```

В `<Имя>Script.script.json` — `"scriptType": "DictionaryCommand"` (всё
остальное как у документа), а в коде первый параметр — ЗАПИСЬ:

```csharp
public partial class <Имя>Command
{
    public override async Task ExecuteAsync(<Справочник> record, CommandContext context)
        => context.AddClientAction(ClientAction.Message(
               await context.GetService<I<Сервис>>().DoAsync(record.MetaId), "success"));
}
```

Пункт меню заводить не надо — кнопка появляется на форме справочника сама.
В тесте: `Db.FindCommandIdAsync("dictionary", "<Имя>")` +
`Db.ExecuteDictionaryCommandAsync(commandId, recordId)` (у документа —
`"document"` / `ExecuteDocumentCommandAsync`). Это и делает такой тест
честным: `FindCommandIdAsync` БРОСАЕТ, когда команды нет, поэтому тест на
кнопку физически не может пройти, пока кнопки не существует.

**Порядок заливки:** `apply-file` команды ДО её скрипта даст
`FOREIGN KEY … MetaScripts` — команда ссылается на `scriptMetaId`. Залей
скрипт, потом повтори файл команды.

`ClientAction.OpenDocument` принимает **`Guid` metaId ТИПА документа**, не имя
(`OpenRecord` — наоборот, строковое имя таблицы). Полный список фабрик —
`src/ZuloOne.Runtime/Commands/ClientAction.cs`.

## Конвенции

- **Валидируй через УЖЕ СУЩЕСТВУЮЩИЕ сервисы, не инлайн и не новым
  `ValidateX` на каждую кнопку.** Остаток — `IStockAvailabilityService` /
  `ISalesFulfillmentService`, ячейки — `IStoreCellService`, количество в
  базовой единице — `IItemQuantityConverter` (если `BaseQuantity` ещё ноль),
  налог — `ITaxService.ResolveRateAsync` / `CalculateTax`, период и счета —
  `IGeneralLedgerService`, комплектующие — `IBomService` (только сказать
  «разверните», не писать строки из команды перехода). Команда тонкая:
  reload → спросить сервис → `ClientAction.Message` или целевой подтип.
- **Не зови мутирующие AfterPost API из команды** (`CreateCalculationAsync`,
  `InvoiceOrderAsync`, `CompleteTripAsync`, `CreateAccrualAsync`,
  `IGeneralLedgerService.PostAsync`, `SubmitReturnAsync`): их уже зовут
  события при Save. Второй вызов удваивает счета, налоги и задания.
- **`ITaxReturnService.BuildAsync` / `BuildFromPeriodAsync` — отдельный
  случай, и раньше он стоял в списке выше по НЕВЕРНОЙ причине.** События их не
  зовут: до 2026-09-22 их не звал вообще никто, сервис жил с девятью тестами и
  без единой двери из интерфейса. Нельзя их звать не «потому что уже позвали», а
  потому что они декларацию **СОЗДАЮТ** — кнопка на самой `TaxReturn` плодила бы
  вторую. Правильное место такой команды — справочник-источник (`TaxPeriod`,
  команда `BuildTaxReturn`), а не создаваемый документ.
  Мораль шире одного сервиса: прежде чем поверить «это уже зовут события»,
  **грепни места вызова**. Сервис без вызывающих — такой же дефект, как
  отсутствующий, только выглядит готовым.
- **Одна команда — один переход**: не делай одну команду, которая по
  внутренней логике прыгает между несколькими целевыми подтипами; для каждого
  перехода — своя команда со своей привязкой.
- **Многоязычные подписи команды** — тот же инлайн-механизм, что у
  `caption` объектов (языковой объект `{ "en": …, "ru": … }` на `object` в
  файле 1); не нужен отдельный перевод.
- **`ExecuteAsync` толстым не делай**: проверка + вызов сервиса + переход —
  три строки бизнес-смысла, всё остальное — в сервисе.

## Проверка

Подними `modelVersion` модели. `zuloone-verify` + интеграционный тест, вызывающий команду напрямую (не
`SaveDocumentAsync` с изменённым `Subtype`): невалидный документ остаётся на
исходном подтипе и получает сообщение, валидный — переходит, и движения
целевого подтипа проведены.
