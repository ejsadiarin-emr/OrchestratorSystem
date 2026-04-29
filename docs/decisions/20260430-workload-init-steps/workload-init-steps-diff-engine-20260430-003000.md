# Decision: Init Steps & Diff Engine Interaction

**Date:** 2026-04-30
**Status:** Resolved

## Q21: Init Steps Interaction with Diff Engine

**Decision: Option A — Only run init steps for packages the diff engine actually processes.**

When a package is `Unchanged` (same version already installed), its pre/post init steps are NOT executed. This keeps the model consistent: unchanged = no action, no side effects.

If re-configuration of an unchanged package is needed, use `ForceInstall` which bypasses the diff engine and runs all steps including init steps.

### UI Implication

The workload revision detail page should display a note near unchanged packages: *"Init steps will not run for unchanged packages. Use Force Install to re-configure."* This helps system admins understand when to use Force Install.

### Diff outcomes and init step behavior

| Diff Outcome | PreInit Steps | Package Install | PostInit Steps |
|---|---|---|---|
| Added | Run | Install | Run |
| Changed | Run | Upgrade | Run |
| Removed | — | (Uninstall) | — |
| Unchanged | **Skip** | **Skip** | **Skip** |
| ForceInstall (any) | Run | Install/Upgrade | Run |