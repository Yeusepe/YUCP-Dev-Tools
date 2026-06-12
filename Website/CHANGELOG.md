# Changelog

## 2026-06-12

- `com.yucp.devtools`:
  - Introduced a new Renderer Optimizer pipeline featuring static mesh merging, texture atlas building, and automated shader conversion.
  - Added a dedicated Renderer Optimizer settings window and integrated optimization passes into the build process.
  - Refactored the Companion Tutorial system into a dedicated runtime module with improved validation and UI components.
  - Added comprehensive unit tests for tutorial injection, serialization, and validation.
  - Updated the Package Exporter UI and building logic to support the enhanced tutorial system.

## 2026-05-30

- Added a public changelog for the VPM listing so VRChat Creator Companion can expose release notes through each package's `changelogUrl`.
- Enhanced PowerShell git hooks: `pre-commit.ps1` now handles automated changelog updates and `commit-msg.ps1` has been updated for better CodeRabbit review and commit message generation.
- `com.yucp.devtools`:
  - Added `changelogUrl` to `package.json` to enable release notes in VRChat Creator Companion.
  - Migrated `DirectVpmInstaller` to use a precompiled runtime DLL for improved performance and reliability.
  - Enhanced `PackageBuilder` and `CompanionTutorialRunner` implementations.
  - Refactored `DirectVpmInstaller` templates and transaction management.
