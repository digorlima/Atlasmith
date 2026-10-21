# Changelog

## 1.0.0 — 2026-10-21

First public release. The beta versions were called DaigoPacker.

### Added

- **Unity package** (`unity/com.daigo.atlasmith`), installable from a git URL:
  - right-click a `.kra`/`.krz` → **Create Atlas** generates the atlas right away,
    with the options stored per Krita file in the sheet itself;
  - its own importer for `.atlasmith.json`, which keeps each sprite's ID by name
    across regenerations;
  - a converter from TexturePacker `.tpsheet` atlases that keeps `spriteID` and
    `fileID`, checks the result sprite by sprite and rolls back on any difference.
- **`.atlasmith.json`**, an open sheet format documented in
  [docs/atlas-format.md](docs/atlas-format.md). The Windows app can write it, the
  `.tpsheet`, or both.
- **`.krz`** files, besides `.kra`.
- **Ignore hidden layers**, on by default.
- A warning before overwriting an atlas when sprites would disappear from it,
  naming the ones that are on hidden layers.
- English interface, chosen automatically from the system language.
- Installer: per-user, no administrator rights, optional desktop and Start-menu
  shortcuts, uninstall from Windows Settings. The same executable also runs portable.
- `--self-check`, which tests the published executable end to end.

### Changed

- Renamed to **Atlasmith**. Settings move to `%AppData%\Atlasmith`; those of the
  betas are copied on first run.
- The `.tpsheet` header names Atlasmith as the writer, instead of copying
  TexturePacker's. TexturePacker Importer refuses such sheets, so before
  overwriting a `.tpsheet` that it reads, the app warns and suggests converting the
  atlas with the Unity package.
- When two Krita layers have the same name, a visible one wins over a hidden one.
- The window reopens where it was on any connected monitor, not only the primary.
- Toggles also respond to screen readers and other accessibility tools.

### Fixed

- Beta 2.1 could fail to start on PCs without a copy of `D3DCompiler_47_cor3.dll` on
  the `PATH`: the package size reduction had removed a native DLL that WPF loads at
  startup. It is back in the package, and `--self-check` now checks for it.

## Betas (DaigoPacker)

### 2.1

- Saved atlases: every generated atlas goes into a list and comes back with one click.
- Reads `.kra` files directly, without Krita's Batch Exporter.

### 2.0

- Rewritten in C# / WPF as a single executable, with nothing to install.
- MaxRects packing (5 heuristics × 5 orderings), preferring balanced shapes:
  on the original UI set, `2048×1024` at 83% occupancy instead of `1024×4096` at 42%.
- Configurable padding, border, extrude and maximum size; light and dark themes.

### Python version

- Script with a Tkinter window and shelf packing.
