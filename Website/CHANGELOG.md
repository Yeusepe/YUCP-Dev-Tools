# Changelog

## 2026-05-30

- Added a public changelog for the VPM listing so VRChat Creator Companion can expose release notes through each package's `changelogUrl`.
- Enhanced PowerShell git hooks: `pre-commit.ps1` now handles automated changelog updates and `commit-msg.ps1` has been updated for better CodeRabbit review and commit message generation.
- `com.yucp.devtools`:
  - Added `changelogUrl` to `package.json` to enable release notes in VRChat Creator Companion.
  - Migrated `DirectVpmInstaller` to use a precompiled runtime DLL for improved performance and reliability.
  - Enhanced `PackageBuilder` and `CompanionTutorialRunner` implementations.
  - Refactored `DirectVpmInstaller` templates and transaction management.
