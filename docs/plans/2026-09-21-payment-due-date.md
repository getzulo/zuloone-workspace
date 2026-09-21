# Payment due date — implementation

Spec: `zulo.one/docs/superpowers/specs/2026-09-21-payment-due-date-design.md`

- Common `PaymentDueService`.
- SalesRealization.DueDate; PurchaseOrder PaymentTerm + DueDate.
- Stamp on save; InvoiceXr / PurchaseOrderXr.
- Common 1.6.0 → 1.7.0; Sales 1.43.0 → 1.44.0; Purchasing 1.8.0 → 1.9.0.
