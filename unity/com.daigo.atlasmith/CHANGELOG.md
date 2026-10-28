# Changelog

## [1.0.1] - 2026-10-27

- The search for assets that use the sprites a regeneration would remove reads
  files as bytes, skips binary ones and keeps an index in `Library/Atlasmith`, so
  only files that changed are read again. In a project with 3.2 GB of scenes,
  prefabs and assets it went from 82 s to about 1 s, and 0.4 s with the index.
- The progress bar follows that search file by file.
- The generation log names the sprites that are new in the atlas.

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
