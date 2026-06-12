# Companion Tutorials

A **companion tutorial** is an optional, whole-Unity overlay walkthrough that you author on an
Export Profile. After a buyer imports your exported `.unitypackage`, the tutorial **auto-plays once**
and then removes itself from their project.

It works in a clean project that does **not** have the YUCP Dev Tools installed: the exporter injects
a self-contained runtime (C# source, wrapped in a per-export-unique namespace so two packages never
collide) plus a self-deleting bootstrap and the overlay helper. The helper ships as a
`YUCPCompanionOverlay.bytes` data asset (never a raw `.exe`, so it doesn't alarm antivirus or buyers);
the runtime extracts it to the project's `Temp/` folder at launch.

> **Platform:** the overlay is **Windows-only**. On macOS/Linux the tutorial is skipped gracefully and
> the injected files still self-clean.

## Authoring

Open **Package Exporter → Companion Tutorial** (or edit the profile asset in the Inspector):

1. Enable the tutorial and give it a title.
2. Add steps. Each step has a **Title**, **Text**, a **Target**, an **Advance When** (wait) condition,
   a **Mouse Action** hint, and an **Overlay Mode**.
3. Use the dropdowns — you don't need to memorize the raw strings below. Choose **Custom (raw)** for
   anything not covered by a preset.
4. Use **Test from here** on a step, **Preview**, or **Run Demo** to see it live (in the dev editor).

Validation runs inline (per-step) and again at export time; problems are reported as a non-blocking
warning so a tutorial never fails an export.

## Target reference

`target` decides what the spotlight points at. Form is either a bare keyword or `prefix:selector`.

| Category            | String                         | Example                       |
|---------------------|--------------------------------|-------------------------------|
| Centered card       | `center`                       | `center`                      |
| Editor window       | *bare keyword*                 | `inspector`, `hierarchy`, `project`, `scene`, `game`, `console`, `animation`, `animator` |
| Toolbar control     | `toolbar:<control>`            | `toolbar:play`, `toolbar:layers` |
| Menu bar item       | `menu:<name>`                  | `menu:yucp`                   |
| Hierarchy object    | `hierarchy:<name or /path>`    | `hierarchy:Main Camera`       |
| Project asset       | `project:<path / name / guid>` | `project:Assets/Foo.prefab`   |
| Scene object        | `scene:<name>`                 | `scene:Main Camera`           |
| Inspector property  | `property:<name>`              | `property:position`           |
| Material property   | `material:<name>`              | `material:shader`             |
| Transform gizmo     | `gizmo`                        | `gizmo`                       |
| UI Toolkit element  | `ui:<element-name>`            | `ui:my-button`                |

A step may instead set a manual `targetRect` (x, y, width, height relative to the main window) under
**Advanced**; both width and height must be non-zero for it to apply.

## Advance-when (`waitFor`) reference

| Condition                 | String                          | Notes                                   |
|---------------------------|---------------------------------|-----------------------------------------|
| Manual (Next button)      | `manual`                        | default                                 |
| Delay                     | `delay:<seconds>`               | e.g. `delay:2`                          |
| Any selection change      | `selection`                     |                                         |
| Asset exists at path      | `assetExists:<assetPath>`       | `assetExists:Assets/Foo.asset`          |
| Package installed         | `packageInstalled:<name>`       | `packageInstalled:com.example.pkg`      |
| Component exists in scene  | `componentExists:<Type>`        | `componentExists:Namespace.MyBehaviour` |
| Selected transform moves  | `transformMoved:<name|selected>`| blank/`selected` = active selection     |

## Mouse action / overlay mode

- **Mouse Action** (visual hint only): `none`, `click`, `doubleClick`, `rightClick`, `drag`.
- **Overlay Mode**: `intrusive` (dims/highlights Unity) or `unintrusive` (shows only the cursor + card).

## How delivery works (for maintainers)

The canonical runtime lives in `Editor/PackageExporter/CompanionRuntime/`
(assembly `YUCP.CompanionTutorial.Source`, engine-only references so it stays self-contained) under the
namespace marker `YUCP.CompanionTutorial.Generated.Source`. This same code powers the in-editor
Preview. At export, `PackageBuilder.TryInjectCompanionTutorial`:

1. reads the runtime `.cs` files and swaps the marker namespace to `YUCP.CompanionTutorial.Generated_<guid>`;
2. injects `Templates/CompanionBootstrap.cs.txt` (an `[InitializeOnLoad]` + `AssetPostprocessor` that
   plays the tutorial once, guarded by an `EditorPrefs` key, then deletes the whole injected
   `Companion/` folder); and
3. ships `Binaries/CompanionOverlay/YUCPCompanionOverlay.exe` as `YUCPCompanionOverlay.bytes`.

There is **no precompiled DLL** — the runtime is reproduced from source on every export.
