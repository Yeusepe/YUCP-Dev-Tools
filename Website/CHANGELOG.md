# Changelog

## 2026-07-28

- `com.yucp.devtools` 0.3.18:
  - Removed the unfinished Renderer Optimizer, which referenced a `RendererOptimizerMarker` component that was never shipped and caused package compilation to fail for clean installs.
  - Fixed derived FBX reconstruction so the exported project-relative base path fallback is used when the original GUID cannot be resolved.
  - Improved derived FBX failure messages with actionable GUID, fallback-path, missing-file, and hash-mismatch details.

## 2026-07-27

- `com.yucp.devtools`:
  - Removed the signing section (sign-in, certificates, product links) and the license-protection toggle from the Package Exporter. Licensed distribution moved to the Creator Companion bootstrap, so a license-locked direct export could not be imported by anyone; existing profiles with the old toggle enabled now export normal, importable packages. The sign-in stack remains for a future direct Unity to Creator Assistant upload flow.
  - Fixed Package Exporter sign-in, which authenticated as the consumer package broker and so could never be granted the certificate scope it needs. It now uses its own dedicated OAuth client and names the API resource on every token exchange.
  - Fixed being signed out at random: concurrent callers now share a single token refresh instead of each redeeming a rotating refresh token, which the server treated as replay.
  - Fixed an editor freeze when publishing to backstage with an expired session.
  - Signing out now revokes the session on the server instead of only clearing it locally.
  - Sped up the signing UI, which previously decrypted the stored session on every repaint.

## 2026-06-15

- Updated GitHub Actions workflows for building listings and releases.
- `com.yucp.devtools`:
  - Overhauled the Companion Tutorial UI in the Package Exporter with new dedicated components, styling, and token management.
  - Enhanced the Package Signing workflow, significantly improving OAuth authentication, registry section handling, and the signing UI.
  - Added new signing trust defaults to the package signing data.

## 2026-06-12

- `com.yucp.devtools`:
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
