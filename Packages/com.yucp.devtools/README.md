# YUCP DevTools

Development tools for YUCP package creators and avatar variant management.

## Features

### Package Exporter
- **Profile-Based Export System** - Save and reuse export configurations
- **Assembly Obfuscation** - Automatic ConfuserEx integration with customizable presets
- **Custom Package Icons** - Inject custom icons into .unitypackage files
- **Dependency Scanner** - Auto-detect VPM and Unity package dependencies
- **Ignore System** - .yucpignore files for excluding folders/files (like .gitignore)
- **Export Inspector** - Preview all assets before exporting

### Model Revision Manager (Beta)
- **Variant Management** - Manage multiple avatar versions from a single base
- **Blendshape Transfer** - Automatically transfer blendshapes between model revisions
- **Bone Mapping** - Intelligent bone path resolution across variants
- **Override System** - Per-variant material, mesh, and component overrides
- **VRCFury Integration** - Build-time processing with VRCFury actions
- **Visual Inspector** - Tree view of all variants and their configurations

## Installation

### Via VCC (Recommended)

1. Add this VPM repository to your VRChat Creator Companion:
   ```
   https://dev.vpm.yucp.club/index.json
   ```

2. Open your project in VCC
3. Click "Manage Project"
4. Find "YUCP DevTools" and click "+" to install
5. YUCP Components will be installed automatically as a dependency

### Manual Installation

1. Download the latest `.unitypackage` from [Releases](https://github.com/Yeusepe/YUCP-Dev-Tools/releases)
2. Import into your Unity project
3. Install YUCP Components from https://vpm.yucp.club

## Dependencies

This package requires:
- **YUCP Components** >= 0.2.9 (automatically installed via VPM)
- **VRChat SDK3 Avatars** (automatically installed via VPM)
- Unity 2022.3.x

## Usage

### Package Exporter

1. Access from menu: `Tools > YUCP > Package Exporter`
2. Create an Export Profile (Assets > Create > YUCP > Export Profile)
3. Configure folders, assemblies, and dependencies
4. Click "Export Package" to build your .unitypackage
5. Optional: Enable obfuscation and custom icons

### Model Revision Manager

1. Create a ModelRevisionBase (Assets > Create > YUCP > Model Revision Base)
2. Add ModelRevisionVariant component to your avatar variants
3. Access manager from menu: `Tools > YUCP > Model Revision Manager`
4. Configure blendshape mappings and bone transfers
5. Build your avatar - transfers process automatically

## Documentation

For detailed documentation:
- Package Exporter Guide: See `/Editor/PackageExporter/Templates/README_Template.md`
- Model Revision Manager: https://github.com/Yeusepe/YUCP-Dev-Tools
- Hover over fields in Unity for tooltips

## Support

- GitHub Issues: https://github.com/Yeusepe/YUCP-Dev-Tools/issues
- YUCP Components: https://github.com/Yeusepe/YUCP-Components

## License

MIT License - See LICENSE.md

