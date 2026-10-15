# YUCP DevTools

Development tools for YUCP package creators.

![YUCP DevTools](Website/banner.png)

## Features

### Package Exporter

#### Core Export Features
- **Profile-Based Export System** - Save and reuse export configurations with customizable metadata
- **Bundled Profiles** - Create composite packages by bundling multiple export profiles together
- **Bulk Export Operations** - Export multiple profiles simultaneously with batch processing
- **Export Inspector** - Preview all assets before exporting with tree view and filtering
- **Profile Organization** - Organize profiles with folders and tags (preset + custom tags)
- **Profile Statistics** - Track export count and last export timestamp per profile

#### Version Management
- **Auto-Increment Version** - Automatically bump version numbers after export
- **Version Increment Strategies** - Choose major, minor, or patch increment
- **Custom Version Rules** - Create custom versioning patterns (semver, date-based, build numbers, etc.)
- **@bump Directives** - Scan code files for `@bump` directives and auto-increment versions in-place
- **Version Directive Scanner** - Project-wide scanning for version directives

#### Assembly Obfuscation
- **ConfuserEx Integration** - Automatic ConfuserEx installation and integration
- **Preset System** - Built-in presets: Minimal, Normal, Aggressive
- **Advanced Settings** - Fine-grained control over individual protections:
  - Control flow obfuscation with intensity control
  - Reference proxy with configurable depth
  - Constants encryption
  - Resource protection
  - Invalid metadata injection
- **IL2CPP Compatibility** - IL2CPP-specific optimizations
- **Reflection Preservation** - Options to preserve names for reflection-dependent code
- **Debug Symbol Stripping** - Remove debug symbols from obfuscated assemblies

#### Derived FBX Export
- **HDiff Binary Patching** - Export FBX variants as small binary patches using HDiff algorithm
- **Multi-Base Patching** - Create derived FBX patches against multiple base FBXs for robust reconstruction
- **Reference Preservation** - Maintain prefab compatibility with original FBX GUIDs
- **Automatic Patch Application** - Patches applied automatically on package import via YUCPPatchImporter

#### Dependency Management
- **Automatic Dependency Scanner** - Auto-detect VPM and Unity package dependencies
- **Dependency Export Modes** - Configure how dependencies are handled (include, exclude, reference)
- **Package.json Generation** - Automatic generation of package.json with dependencies
- **VPM Dependency Resolution** - Full VPM dependency tree scanning

#### Ignore & Filtering System
- **.yucpignore Files** - Gitignore-style exclusion patterns
- **Permanent Ignore Folders** - Profile-level folder exclusions (persistent across scans)
- **File Pattern Filters** - Wildcard-based file exclusion (e.g., `*.tmp`, `*.log`)
- **Folder Name Filters** - Exclude folders by name (e.g., `.git`, `Temp`)

#### Media Management
- **Custom Package Icons** - Inject custom icons into .unitypackage files
- **Package Banners** - Display custom banners in the exporter window

#### Bulk Operations
- **Bulk Profile Editor** - Edit multiple profiles simultaneously:
  - Set version across all selected profiles
  - Add/remove folders in bulk
  - Configure dependencies in bulk
  - Apply obfuscation settings to multiple profiles
  - Batch version increment strategies

#### Keyboard Shortcuts
- **Export Shortcuts**: `Ctrl/Cmd+E` (export selected), `Ctrl/Cmd+Shift+E` (export all), `Ctrl/Cmd+Enter` (quick export)
- **Profile Management**: `F2` or `Enter` (rename), `Ctrl/Cmd+D` (duplicate), `Delete/Backspace` (delete)
- **Navigation**: Arrow keys, `Home/End`, `PageUp/PageDown` for profile navigation
- **Search**: `Ctrl/Cmd+F` (focus search)
- **Utilities**: `F5` (refresh), `Ctrl/Cmd+Alt+P` (select in project), `Ctrl/Cmd+Alt+E` (show in explorer)
- **Standard**: `Ctrl/Cmd+Z` (undo), `Ctrl/Cmd+Y` or `Ctrl/Cmd+Shift+Z` (redo)

#### Templates & Utilities
- **Export Profile Templates** - Pre-configured templates for quick setup
- **Direct VPM Installer** - Install VPM dependencies directly from packages
- **Backup System** - Automatic prefab backup before patch application
- **Installation Health Tools** - Validate, repair, and clean installation artifacts

### Texture Array Builder

#### Source Modes
- **Folder Input** - Automatically discover textures from a folder
- **Manual List** - Manually add textures to the array
- **Animated GIF** - Extract frames from animated GIF files
- **Video Clip** - Extract frames from Unity VideoClip assets with frame stepping

#### Processing Options
- **Resolution Controls** - Set target width/height with aspect ratio preservation
- **Texture Formats** - Support for multiple formats:
  - DXT1 (BC1) - 4bpp RGB, desktop, no alpha
  - DXT5 (BC3) - 8bpp RGBA, desktop
  - BC7 - 8bpp RGBA, high quality desktop
  - ETC2 RGBA8 - 4bpp RGBA, modern mobile
  - ASTC 6x6 - ~3.6bpp RGBA, high-end mobile
  - PVRTC RGBA4 - 4bpp RGBA, iOS
  - RGBA32 - 32bpp, uncompressed fallback
- **Mipmap Generation** - Optional mipmap generation
- **Color Space** - Linear or sRGB color space options
- **Alpha Detection** - Automatic alpha channel detection and format selection

#### Preview System
- **Texture Preview** - Preview all textures before building
- **Array Preview** - Visual preview of the final texture array

### Other Utilities

#### Installation Tools
- **Validate Install** - Check YUCP installation health
- **Repair Install** - Automatically repair installation issues
- **Clean Import Artifacts** - Remove temporary installer files and artifacts
- **Fix Self-Referencing Dependencies** - Repair circular dependency issues

#### Development Tools
- **Derived FBX Debug Window** - Debug window for derived FBX operations
- **Patch Cleanup** - Tools for cleaning up patch-related files

## Installation

### Via VCC (Recommended)

1. Add this VPM repository to your VRChat Creator Companion:
   ```
   https://vpm.yucp.club/index.json
   ```

2. Open your project in VCC
3. Click "Manage Project"
4. Find "YUCP DevTools" and click "+" to install
5. YUCP Components, YUCP UI, and YUCP Motion will be installed automatically as dependencies

### Manual Installation

1. Download the latest `.unitypackage` from [Releases](https://github.com/Yeusepe/YUCP-Dev-Tools/releases)
2. Import into your Unity project
3. Install [YUCP Components and YUCP UI](https://vpm.yucp.club/) and [YUCP Motion](https://dev.vpm.yucp.club/) as dependencies

## Requirements

- Unity 2022.3 or later
- [YUCP Components](https://vpm.yucp.club) >= 0.3.45 (automatically installed via VPM)
- [YUCP UI](https://vpm.yucp.club/) >= 0.1.0 (automatically installed via VPM)
- [YUCP Motion](https://dev.vpm.yucp.club/) >= 0.1.0 (automatically installed via VPM)
- VRChat SDK3 Avatars (automatically installed via VPM)
- VRCFury

### Optional Dependencies

- **Unity FBX Exporter** - Required for Kitbash synthetic base generation (install via Package Manager: `com.unity.formats.fbx`)
- **ConfuserEx** - Automatically downloaded when obfuscation is first used

## 🔧 Usage

### Package Exporter

#### Quick Start
1. Open from menu: `Tools > YUCP > Package Exporter`
2. Create an Export Profile: `Assets > Create > YUCP > Export Profile`
3. Configure folders to export
4. Click "Export Package" to build your .unitypackage

#### Creating Export Profiles
- **Via Asset Menu**: `Assets > Create > YUCP > Export Profile`
- **Via Exporter Window**: Click "New Profile" in the sidebar
- **In Current Folder**: `Assets > Create > YUCP > Export Profile Here`

#### Advanced Features
- **Bundled Profiles**: Add other export profiles to bundle their assets together
- **Custom Version Rules**: Create custom versioning with `Assets > Create > YUCP > Custom Version Rule`
- **@bump Directives**: Add `// @bump rule_name` comments in code files for automatic version bumping
- **Derived FBX**: Mark FBX files as derived in Model Importer to export as derived FBXs

#### Keyboard Shortcuts
- `Ctrl/Cmd+E` - Export selected profiles
- `Ctrl/Cmd+Shift+E` - Export all profiles
- `Ctrl/Cmd+Enter` - Quick export current profile
- `F2` or `Enter` - Rename profile
- `Ctrl/Cmd+D` - Duplicate profile
- `Delete/Backspace` - Delete profile
- `Ctrl/Cmd+F` - Focus search
- `F5` - Refresh profiles

### Texture Array Builder

#### Quick Start
1. Open from menu: `Tools > YUCP > Others > Development > Texture Array Builder`
2. Choose source mode (Folder, Manual List, GIF, or Video)
3. Configure processing settings (resolution, format, mipmaps)
4. Preview textures
5. Build the texture array

## Documentation

For detailed documentation:
- **Package Exporter Guide**: See `/Editor/PackageExporter/Templates/README_Template.md`
- **Custom Version Rules**: See `/Editor/PackageExporter/CUSTOM_VERSION_RULES.md`
- **CLI & GitHub Actions**: See `/Editor/PackageExporter/CLI-and-GitHub-Actions.md`
- **Version Rule Examples**: See `/Editor/PackageExporter/Examples/README.md`
- **YUCP Components**: https://github.com/Yeusepe/YUCP-Components

## Support

- **GitHub Issues**: [GitHub Issues](https://github.com/Yeusepe/YUCP-Dev-Tools/issues)
- **Main Package**: [YUCP Components](https://github.com/Yeusepe/YUCP-Components)
- **VPM Listing**: https://dev.vpm.yucp.club

## License

MIT License - See LICENSE.md

---

**Made with ❤️ by YUCP Club**
