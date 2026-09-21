# TaxProfile / TaxMapping — план реализации

**Цель:** профиль стороны и маппинг источника выбирают налоговый код без замены `TaxRule`.
**Подход:** три справочника в Tax; движок обогащает контекст и подставляет mapping между правилом и `DefaultTaxCode`. Sales только дописывает id в уже существующий словарь.
**ТЗ:** `docs/superpowers/specs/2026-09-21-tax-profile-mapping-design.md` в zulo.one.

## Модели и слои
| Объект | Модель-владелец | Слой | Нужна зависимость |
|---|---|---|---|
| TaxProfile | Tax | Base(1) | — |
| TaxProfileAttribute | Tax | Base(1) | — |
| TaxMapping | Tax | Base(1) | — |
| TaxService | Tax | Base(1) | — |
| SalesInvoiceEventHandler.TaxContextAsync | Sales | Solution(2) | Tax ≥ 1.17.0 (уже есть) |

## metaId
| Имя | metaId |
|---|---|
| TaxProfileSeq | 916c20c0-ca27-4ba8-b34d-de089520e559 |
| TaxProfile | 122ec1c2-989b-4372-8e78-257f200d7a3b |
| RefTaxProfile | ea35fe0a-bef2-4e1a-83ce-b6b08ba91247 |
| TaxProfileAttributeSeq | a0d9efe3-d1d3-4683-8e11-b18b5d51ca61 |
| TaxProfileAttribute | 70a1c4e2-7577-4919-832c-e3890a933c27 |
| TaxMappingSeq | 12e0cad9-80ee-4c9a-bbc7-0f25a017cebc |
| TaxMapping | 1a00e49e-1724-4fd5-bcbe-d16118743e5e |
| TaxProfileMappingTest | d95e6893-6686-49ca-8e3f-4b16529dcedf |

## Строковые имена
| Что | Имя |
|---|---|
| PartyType | LegalEntity / Customer / Supplier |
| SourceType | Item / ItemGroup / Customer / Supplier / LegalEntity |
| Context | buyer.id, seller.id, supplier.id, item.id, item.groupId, buyer.profile.*, seller.profile.*, supplier.profile.* |
| IFoo | FindOverlappingProfileAsync, FindOverlappingMappingAsync |

Bump Tax 1.16.0 → 1.17.0, Sales 1.41.0 → 1.42.0.
