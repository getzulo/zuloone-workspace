# Auto-receive on PlaceOrder — план

**Цель:** флаг `AutoReceiveOnOrder` проводит приход с кнопки «Заказать».
**Подход:** `IPurchaseReceiptService` — общая приёмка; PlaceOrder оркестрирует Ordered затем Received.
**ТЗ:** `d:\Sources\zulo.one\docs\superpowers\specs\2026-09-21-auto-receive-on-order-design.md`

## GUIDs

| Объект | metaId |
|---|---|
| PurchaseReceiptService | 7c1e4a82-0b59-4d63-9e17-2f8a5c0d4b91 |
| PurchaseReceiptService.script | 8d2f5b93-1c6a-4e74-8f28-3a9b6d1e5c02 |

Purchasing 1.9.0 → 1.10.0
