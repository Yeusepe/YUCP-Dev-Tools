# Changelog

All notable changes to YUCP DevTools will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.20] - 2026-07-30

### Changed
- Added a source-controlled, reproducible build path for the direct VPM installer runtime.
- Preserved complete signed alias-v2 bootstrap intents when Unitypackage bootstraps hand off to the importer.
- Recognized alias-v2 packages in dependency scanning and signing-root resolution.

## [0.3.19] - 2026-07-28

### Fixed
- Fixed DevTools compilation against the current importer by removing the obsolete `ProtectedPayloadManifestEntry` dependency from the signing manifest.
- Kept signing and verification canonicalization aligned by omitting the unused legacy `protectedPayloads` field; protected asset registration continues through the current server registration flow.

## [0.3.18] - 2026-07-28

### Changed
- Removed the unfinished Renderer Optimizer and its unused Mesh Optimizer folder structure from the distributed editor package.

### Fixed
- Fixed a package compilation failure caused by `RendererOptimizerBuildProcessor` referencing a `RendererOptimizerMarker` component that was never included in the package.
- Derived FBX reconstruction now uses the exported project-relative base path fallback when its original asset GUID cannot be resolved, and reports more actionable GUID, file, and hash diagnostics when reconstruction cannot continue.

## [0.3.17] - 2026-07-28

### Changed
- Package Exporter no longer writes an `alias-v1` package shell. Every export is now fully self-contained and imports offline; server-mediated delivery is only authorized by the server when a package is installed from the creator's VPM URL. Any pre-existing alias contract is stripped from the exported `package.json`.
- Rebuilt the precompiled `YUCP.PatchRuntime` and `YUCP.DirectVpmInstaller.Runtime` binaries.
- Split patch-import staging out of `PackageBuilder` into dedicated `PatchImportPackageInjector` and `UnityPackageStagingWriter` helpers.
- Improved derived FBX failure diagnostics: errors now name the required base GUID, report the direct path fallback status, and show the hash check, along with a concrete fix suggestion.

### Added
- Advanced derived FBX export option to include direct project-relative base path fallbacks, used only when GUID lookup fails. A profile validation warning explains the trade-off.
- `PatchRuntimeCompiler` editor tooling (Tools > YUCP > Others > Package Exporter > Rebuild Patch Runtime DLL, plus a command-line entry point) to rebuild the precompiled patch runtime from source.

### Fixed
- Duplicate-assembly errors ("has the same filename as Assembly Definition File") when importing a package over one exported by an older version: the patch importer now purges the legacy source-based runtime left behind in `Packages/com.yucp.temp/Editor` before processing patches.
- VCC listing a repository source twice after a direct install: the installer now matches existing sources by normalized URL (scheme, host, and path) instead of exact string, and no longer rewrites a source the user already has.
