# Atlasmith for Unity

Sprite atlases straight from Krita files.

- Right-click a `.kra` or `.krz` in the Project window → **Create Atlas**: the layers
  named in the `name-name` pattern become sprites, packed into a PNG plus an
  `.atlasmith.json` sheet next to the Krita file.
- The bundled importer turns that sheet into sprites and keeps each sprite's ID by
  name, so scenes, prefabs and animations keep their references after every
  regeneration.
- *Tools → Atlasmith → Convert TexturePacker Sheets…* converts `.tpsheet` atlases
  without breaking references.

Install from *Window → Package Manager → + → Add package from git URL…*:

```
https://github.com/digorlima/Atlasmith.git?path=/unity/com.daigo.atlasmith
```

Full documentation, the standalone Windows app and the sheet format are in the
[repository README](../../README.md).
