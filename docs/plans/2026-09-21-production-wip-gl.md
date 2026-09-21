# Production WIP GL — implementation

Spec: `zulo.one/docs/superpowers/specs/2026-09-21-production-wip-gl-design.md`

- AccountingSettings WipAccount / WipAccountCode, stamp on save.
- ProductionOrder.GLIntegration: Released Dr WIP / Cr Inv; Finished inverse
  only if the WIP journal exists.
- ProductionWipGLTest: release, finish, jump, empty profile.
- Accounting 1.6.0 → 1.7.0; GLIntegration 1.2.0 → 1.3.0.
