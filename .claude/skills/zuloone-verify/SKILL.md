---
name: zuloone-verify
description: Обязательный цикл проверки изменений бизнес-слоя ZuloOne — синк, компиляция, схема, интеграционные тесты. Use after ANY change to workspace files before declaring work done.
---

# Проверка изменений

Правка файла воркспейса «сделана» только когда прошла все четыре ступени.
Сервер dev-стенда: `http://localhost:5257` (ядро в docker: контейнер
`zuloone-core-1`).

## 1. Синк применил файл (~2–4 с после сохранения)

```bash
docker logs zuloone-core-1 --since 2m 2>&1 | grep "Workspace sync"
```

- `<файл> applied (N created, M updated…)` — применилось.
- `Workspace sync: <warning>` — что-то проигнорировано (сирота без envelope,
  Core-гейт, неизвестный вид) — разберись, прежде чем идти дальше.
- Ничего не появилось — файл не входит в отслеживаемые (`.json`/`.cs`/`.vt`)
  или лежит в игнорируемой папке.
- Если правку ОТКЛОНИЛИ (Core, политика безопасности) — экспорт перепишет файл
  обратно; `git diff` покажет откат.

## 2. Компиляция моделей

```bash
curl -s -X POST http://localhost:5257/api/metadata/models/compile
```

Каждая модель должна быть `"status":"Ok"`. `Failed` несёт первую ошибку
компилятора с именем скрипта; `DependencyFailed` — сломана зависимость.
Типовые причины: опечатка в имени свойства сущности (сверься с
`.generated/Entities/<Имя>.cs`), дубль имени класса в модели, объявленная база
у partial-скрипта, которому её генерит платформа.

## 3. Схема БД

Новые объекты/поля требуют физических таблиц:

```bash
curl -s -X POST http://localhost:5257/api/schema/sync
```

Посмотреть, что будет сделано, не делая: `GET /api/schema/detect`.

## 4. Интеграционные тесты

На каждый бизнес-сценарий — тест в `Tests/` своей модели (пара `Имя.json` +
`Имя.cs`):

```csharp
using ZuloOne.Runtime.Testing;

public class <Имя>Test : IntegrationTestScriptBase
{
    [IntegrationTest("<Что проверяем>")]
    public async Task Scenario()
    {
        var wh = Db.NewId();
        // создать запись/документ, провести, проверить остатки
        await Db.PostMovementAsync("<Регистр>", DateTime.UtcNow.Date,
            new Dictionary<string, object?> { ["Warehouse"] = wh },
            new Dictionary<string, decimal> { ["Quantity"] = 7m });
        Assert.IsTrue(/* … */, "сообщение с {0}", значение);
    }
}
```

Запуск:

```bash
curl -s -X POST http://localhost:5257/api/metadata/tests/run-all      # все
curl -s -X POST http://localhost:5257/api/metadata/tests/<GUID>/run   # один
```

## Быстрая диагностика

| Симптом | Куда смотреть |
|---|---|
| Файл «не применяется» | шаг 1: лог синка; envelope рядом с .cs есть? kind верный? |
| CS0246 «тип не найден» | объект ещё не создан/не скомпилирован — шаги 1–2 по порядку |
| 500 при записи данных | `docker logs zuloone-core-1 --since 5m` — реальный стек |
| Проведение без движений | скрипт привязан не к тому подтипу (`objectMetaId`) |
| Правка «откатилась» | Core-гейт или политика безопасности — шаг 1, лог |

Полный прогон платформенного бенча (если стенд разработческий):
`POST /api/dev/totals-testbench` — не обязан быть зелёным из-за твоих правок
бизнес-моделей, но не должен ЛОМАТЬСЯ ими.
