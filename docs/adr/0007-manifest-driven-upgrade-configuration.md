# Admin-Mandatory UpgradeBehavior in Package Manifests

We considered auto-populating `UpgradeBehavior` and `UninstallArgs` via a pattern registry that maps package name patterns (e.g., `python-*`) to default values. This would reduce boilerplate for common packages.

We rejected this approach. Every package manifest must explicitly declare `upgradeBehavior` and `uninstallArgs` (where applicable). The orchestrator rejects packages at creation/import time if `upgradeBehavior` is missing.

**Rationale:**
- **Silent misconfiguration risk:** A package not in the registry would silently default to `InPlace`, potentially causing the exact old-version-persistence bug this system is designed to prevent.
- **Version-specific behavior:** A package's upgrade behavior can change across versions. A pattern registry cannot capture `python-3.x` vs `python-4.x` behavioral differences without excessive complexity.
- **Manifest as source of truth:** The manifest JSON is the contract between the admin and the system. Hiding defaults in code makes debugging harder and violates principle of least surprise.
- **Maintenance burden:** The registry would require ongoing curation as new package versions and installer types emerge.

Pattern documentation (like the table in `docs/decisions/update-mode-deep-dive.md`) remains as guidance for admins, but it is advisory only — never applied automatically.
