# Changelog

## [1.0.0] - 2026-10-21

First release.

- **Create Atlas** on `.kra`/`.krz` files, generating right away with the default
  options (maximum size 4096, padding 5, power of two, hidden layers left out). The
  options are stored per Krita file in its `.atlasmith.json`.
- Importer for `.atlasmith.json` that keeps sprite IDs by name across
  regenerations, along with pivots and borders edited in the Sprite Editor.
- Before a regeneration removes sprites, a dialog names them, says which are on
  hidden layers and which are used by assets in the project.
- Converter from TexturePacker `.tpsheet` atlases: keeps `spriteID` and `fileID`,
  backs up the files, checks every sprite and rolls back on any difference.
- English and Brazilian Portuguese windows.
