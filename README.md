# Atlasmith

Sprite atlases straight from Krita files, for Unity or any engine.

*[Leia em português](README.pt-BR.md)*

Draw your sprites as layers in one Krita file, name them like `menu-tab-active`,
and Atlasmith packs them into a single PNG plus a small sheet that says where each
sprite is. No export step, no folder of loose PNGs to keep in sync.

It comes in two parts that share the same packing code and produce the same atlas:

- **Atlasmith for Windows**: a standalone app, for artists who never open Unity
  and for any other engine.
- **The Unity package**: right-click a `.kra` or `.krz` in the Project window and
  choose **Create Atlas**. It brings its own importer, and converts existing
  TexturePacker atlases without breaking the references to their sprites.

## Features

- Reads `.kra` and `.krz` files directly. Layers named in the `name-name` pattern
  become sprites; a group with such a name becomes one sprite with its children
  composited.
- **Ignore hidden layers** (on by default): layers with the eye closed in Krita stay
  out of the atlas.
- Also packs a folder of images (PNG, JPG, BMP, GIF, TIFF, TGA).
- MaxRects packing with several heuristics, keeping the smallest and most balanced
  result: `2048×1024` rather than `1024×4096`.
- Writes PNG + [`.atlasmith.json`](docs/atlas-format.md), an open format any engine
  can read, and optionally a `.tpsheet` in the same layout TexturePacker uses
  (see [the note below](#a-note-on-tpsheet)).
- Before overwriting an atlas, warns about sprites that would disappear, and
  whether they are on hidden layers: in Unity, whatever uses them would lose the
  reference.
- English and Brazilian Portuguese, following the system language; light and dark
  themes following Windows.

## Atlasmith for Windows

### Install

Download from the [Releases](../../releases) page:

- **`Atlasmith-1.0.0-setup.exe`**: installs for your user only, no administrator
  rights needed, in `%LocalAppData%\Programs\Atlasmith`. Desktop and Start-menu
  shortcuts are optional. Uninstall from *Settings → Apps*.
- **`Atlasmith.exe`**: portable. Copy it anywhere and run it.

Both are the same self-contained file, so nothing else has to be installed (no
.NET, no Python). Windows 10 or 11, x64.

> The executables aren't code-signed yet, so Windows SmartScreen may say *"Windows
> protected your PC"*. Click **More info → Run anyway**.

### Use

1. **Input**: turn on **Read straight from Krita** and choose a `.kra`/`.krz`, or
   choose a folder of images (subfolders are included). Dragging a file or folder
   onto the window also works.
2. **Output**: destination folder, atlas name and sheet format.
3. **Packing**: padding, maximum size and the other options below.
4. **Generate atlas**, or `Ctrl+G`.

Every atlas you generate goes into the **Saved atlases** list. Picking one fills
everything back in, so regenerating after editing the art is one click.

| Option | What it does |
|---|---|
| **Sprite padding** | Empty pixels guaranteed between two sprites. Default 5. |
| **Border padding** | Empty pixels between the sprites and the edge of the atlas. |
| **Extrude** | Repeats each sprite's edge outward, against bleeding with bilinear filtering or mipmaps. The sprite rectangle stays the same. |
| **Maximum size** | Largest width or height, from 512 to 16384. Default 4096. |
| **Power-of-two size** | On by default. When off, the atlas shrinks to the area actually used, rounded up to a multiple of 4. |
| **Force square atlas** | Only considers N×N sizes. |

Settings live in `%AppData%\Atlasmith`. On first run, the settings and saved
atlases of the beta versions (DaigoPacker) are copied over; the old folder is left
untouched.

### Command line

```
Atlasmith.exe [file.kra | file.krz | folder]
Atlasmith.exe --install   [--quiet] [--no-desktop] [--no-start-menu] [--no-launch]
Atlasmith.exe --uninstall [--quiet]
Atlasmith.exe --self-check [report.txt]
```

`--self-check` runs the packaged executable through window creation, rendering
and a full atlas generation, then exits with 0 when everything passed.

## Krita files

- Layers whose name has two or more parts separated by hyphens, without spaces,
  become sprites: `menu-tab-active`, `speech-balloon-A`. "Background", "Layer 1" or
  "logo" are left out.
- A **group** with a name in that pattern becomes one sprite, with its children
  composited. A group with any other name is just organization: the layers inside
  it are read normally.
- Pixels and bounds match what Krita's Batch Exporter would write: the painted area
  of the layer, without the layer's own opacity and blending mode. Transparency
  masks are applied.
- **Ignore hidden layers**: layers with the eye closed in Krita, or inside a hidden
  group, are left out. Turn it off to include them.
- If two layers have the same name, only one is kept (a visible layer wins over a
  hidden one) and the summary names the others.
- What counts is the **saved** file: save in Krita before generating.
- Not supported: vector, filter, fill, clone and file layers (convert them to paint
  layers). Layer styles and filter masks are not applied. The app lists what it
  skipped.
- Color spaces: RGBA and grayscale, 8 or 16 bits per channel. Others (CMYK, Lab,
  floating point) need *Image → Convert Image Color Space* in Krita first.

## Unity package

Requires Unity 2022.3 or newer. It depends on the 2D Sprite package, which the
Package Manager adds automatically.

### Install

In *Window → Package Manager*, click **+ → Add package from git URL…** and paste:

```
https://github.com/digorlima/Atlasmith.git?path=/unity/com.daigo.atlasmith
```

Or add it to `Packages/manifest.json`, pinned to a release:

```json
"com.daigo.atlasmith": "https://github.com/digorlima/Atlasmith.git?path=/unity/com.daigo.atlasmith#v1.0.1"
```

The first time the package is added, Unity reimports every texture in the project
once, as it does for any texture importer (TexturePacker Importer included). In a
large project, add it when you can leave Unity working for a while.

### Create an atlas

Right-click one or more `.kra`/`.krz` files in the Project window and choose
**Create Atlas**. The atlas is generated right away next to the Krita file, as
`<name>.png` + `<name>.atlasmith.json`, with the defaults: maximum size 4096,
padding 5, power-of-two size, hidden layers left out.

The window that opens shows the result (size, occupancy, a preview with sprite
outlines, the log) and the options, so you can change them and regenerate. They
are stored in the `.atlasmith.json` itself (`source` and `settings`): each Krita
file keeps its own, and they travel with the project in version control.

If a regeneration would remove sprites from the atlas, nothing is written until you
decide. The dialog names those sprites, says which ones are on hidden layers, and
which ones are used by scenes, prefabs or other assets, and offers to include the
hidden layers instead.

### The importer

`.atlasmith.json` sheets are imported as sprites of the PNG next to them. Each
sprite keeps its ID by name, so scenes, prefabs and animations keep pointing at
the right sprites after every regeneration, even when the layout changes
completely. A regeneration only moves each sprite's rectangle: pivots and 9-slice
borders edited in the Sprite Editor stay as they are. A sprite that is removed and
later comes back gets its old file ID, so references to it work again; its pivot
and borders start over from the sheet.

The scan for references covers assets serialized as text (scenes, prefabs,
animations, materials, ScriptableObjects, UI Toolkit files). Sprites loaded by name
at runtime, through `Resources` or Addressables, are not detected. The first scan
reads all of those files; what each one references is then kept in
`Library/Atlasmith`, so later scans only reread the files that changed. Binary
files, such as lighting data, store references as bytes and are skipped, and so are
the folders Unity ignores (`.git`, `Samples~`).

### Coming from TexturePacker

*Tools → Atlasmith → Convert TexturePacker Sheets…* (or right-click a `.tpsheet` →
**Convert to Atlasmith**) turns `.tpsheet` atlases into `.atlasmith.json`:

- the new sheet is built from what Unity has now (rectangles, pivots, borders);
- every sprite keeps its `spriteID` and `fileID`, so references survive;
- the result is checked sprite by sprite, and if anything differs the conversion is
  undone;
- the original files are copied to `AtlasmithBackups/`, at the project root, before
  anything changes. Keep that folder out of version control.

After converting, commit the new `.atlasmith.json` files with their `.meta`, the
deleted `.tpsheet` files and the updated `SettingsTexturePackerImporter.txt`. The
textures' `.meta` files don't change.

### A note on `.tpsheet`

TexturePacker Importer only accepts sheets created by TexturePacker itself, and
Atlasmith doesn't pretend to be TexturePacker: a `.tpsheet` written by Atlasmith
says so in its header, and TexturePacker Importer refuses it. For Unity, convert
the atlas with the package and use `.atlasmith.json` from then on. When the
Windows app is about to overwrite a `.tpsheet` that TexturePacker Importer reads,
it warns first and suggests converting. The `.tpsheet` output remains for other
tools that read that layout.

## Building from source

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
.\build.ps1                      # dist\Atlasmith.exe and dist\Atlasmith-<version>-setup.exe
.\build.ps1 -Runtime win-arm64   # ARM64 build
dotnet run --project src\Atlasmith\Atlasmith.csproj
```

```
src/Atlasmith/              Windows app (WPF)
src/Atlasmith.Core/         builds the shared code below as a library for the app
unity/com.daigo.atlasmith/  Unity package
  Editor/Core/              shared code: Krita reader, packer, PNG and sheet writers
tools/
  PackerBench/              packing integration test and comparison with a reference atlas
  KraCheck/                 checks the Krita reader against Krita itself and against existing atlases
  krita/                    scripts that run inside Krita (reference export, test .kra)
  verify_atlas.py           independent pixel-by-pixel check of a PNG + .tpsheet pair
docs/atlas-format.md        the .atlasmith.json format
```

The code under `unity/com.daigo.atlasmith/Editor/Core` is written for C# 9 and
.NET Standard 2.1, so the same files compile in Unity and in the app.

## License

[MIT](LICENSE). Third-party components and trademarks are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Atlasmith is an independent project. It is not affiliated with or endorsed by
CodeAndWeb (TexturePacker), the Krita Foundation or Unity Technologies.
