# Changelog

## 2026-05-30

- Added `changelogUrl` to `package.json` for both `com.yucp.devtools` and `com.yucp.motion` to enable release notes in VRChat Creator Companion.
- Added a public changelog for the VPM listing so VRChat Creator Companion can expose release notes through each package's `changelogUrl`.
- Added PowerShell-based git hooks (`pre-commit` and `commit-msg`) to automate CodeRabbit reviews and public changelog generation.
- `com.yucp.devtools`:
  - Migrated `DirectVpmInstaller` to use a precompiled runtime DLL for improved performance and reliability.
  - Enhanced `PackageBuilder` and `CompanionTutorialRunner` implementations.
  - Refactored `DirectVpmInstaller` templates and transaction management.
