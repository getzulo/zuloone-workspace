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
Без такой команды документ из UI навсегда черновик, сколько бы скриптов
ни висело на «Подтверждено».

Типичный контур: на `Draft` кнопка «Подтвердить» / «Провести» / «Оплатить» —
проверки (есть строки, хватает остатка…), затем `document.Subtype = "Posted"`
и `SaveDocumentAsync`. Прямой `document.Subtype = "..."` из сервиса или теста
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
    "caption": "<English caption>", "caption_ru": "<Русская подпись>",
    "scriptMetaId": "<GUID скрипта — см. файл 2>",
    "parameterMode": "None",
    "displayOrder": 0,
    "beginGroup": false,
    "isEnabled": true,
    "requiresConfirmation": false,
    "reloadAfterExecution": true,
    "metaId": "<GUID-команды>",
    "name": "<Имя>",
    "modelId": "<GUID модели>", "layerId": 1
  },
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
    "modelId": "<GUID модели>", "layerId": 1
  }
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

        document.Subtype = "<ЦелевойПодтипValue>";
        await DocumentManager.SaveDocumentAsync(document);
    }
}
```

Проверяй сначала, переходи — только если проверка прошла. Не проходит —
`CreateUserMessage("причина")` и `return` без изменения подтипа: пользователь
видит сообщение, документ остаётся на месте.

## Конвенции

- **Валидируй через сервис, не инлайн**: логика проверки (есть ли строки,
  хватает ли остатка на складе…) живёт в `I<Имя>Service` (`zuloone-new-service`),
  команда только вызывает её и решает, переходить или нет — та же логика
  часто нужна и другим командам/событиям.
- **Одна команда — один переход**: не делай одну команду, которая по
  внутренней логике прыгает между несколькими целевыми подтипами; для каждого
  перехода — своя команда со своей привязкой.
- **Многоязычные подписи команды** — тот же инлайн-механизм, что у
  `caption` объектов (`caption`/`caption_ru`/`caption_ar` на `object` в
  файле 1); не нужен отдельный перевод.
- **`ExecuteAsync` толстым не делай**: проверка + вызов сервиса + переход —
  три строки бизнес-смысла, всё остальное — в сервисе.

## Проверка

`zuloone-verify` + интеграционный тест, вызывающий команду напрямую (не
`SaveDocumentAsync` с изменённым `Subtype`): невалидный документ остаётся на
исходном подтипе и получает сообщение, валидный — переходит, и движения
целевого подтипа проведены.
