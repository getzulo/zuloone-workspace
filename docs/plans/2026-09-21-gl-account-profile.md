# Профиль счетов GL — план реализации

**Цель:** в настройках учёта выбирают счёт плана, а не печатают код.
**Подход:** новые Ref-поля рядом со строками; save штампует Code; PostAsync не трогаем.
**ТЗ:** `docs/superpowers/specs/2026-09-21-gl-account-profile-design.md`

## Модели и слои
| Объект | Модель-владелец | Слой | Нужна зависимость |
|---|---|---|---|
| AccountingSettings | Accounting | Base(1) | — |
| RefChartOfAccounts | Accounting | Base(1) | ChartOfAccounts |

## metaId
| Имя | metaId |
|---|---|
| RefChartOfAccounts | 6157dcf8-a8d6-4b52-ba00-83679883938d |
| AccountingAccountProfileTest | 3c8f1a20-6b4d-4e91-9c07-2a5e8d1b4f63 |

Bump Accounting 1.5.0 → 1.6.0.
