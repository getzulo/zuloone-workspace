# Delivery trip plan vs actual — implementation

Spec: `zulo.one/docs/superpowers/specs/2026-09-21-delivery-trip-times-design.md`

- Header PlannedDepart / ActualDepart / ActualComplete.
- Line PlannedFromMinutes / PlannedToMinutes / DwellMinutes.
- Fill copies windows; Dispatch stamps plan+actual; Complete stamps complete before read-only.
- DeliveryFleetTest + DeliveryTripXr.
- Sales 1.42.0 → 1.43.0.
