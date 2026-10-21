# The `.atlasmith.json` sheet format

*[Português](#em-português) abaixo.*

Atlasmith writes every atlas as two files side by side:

```
menu-atlas.png
menu-atlas.atlasmith.json
```

The PNG is a plain 32-bit RGBA image. The JSON sheet says where each sprite is.
The format is deliberately small, so that any engine or script can read it
without a library. The Unity package in this repository ships an importer for
it; for other engines, a reader is a few dozen lines.

## Example

```json
{
  "format": "atlasmith",
  "version": 1,
  "generator": "Atlasmith 1.0.0",
  "texture": "menu-atlas.png",
  "size": {"w": 2048, "h": 1024},
  "source": {
    "type": "krita",
    "path": "menu-ui-project.kra",
    "ignoreHiddenLayers": true
  },
  "settings": {
    "padding": 5,
    "borderPadding": 0,
    "extrude": 0,
    "maxSize": 4096,
    "powerOfTwo": true,
    "forceSquare": false
  },
  "sprites": [
    {"name": "menu-tab-active", "x": 0, "y": 0, "w": 312, "h": 96},
    {"name": "menu-panel", "x": 317, "y": 0, "w": 640, "h": 480,
     "pivot": {"x": 0.5, "y": 1},
     "border": {"left": 24, "top": 24, "right": 24, "bottom": 24}}
  ]
}
```

## Fields

| Field | Type | Required | Meaning |
|---|---|---|---|
| `format` | string | yes | Always `"atlasmith"`. |
| `version` | integer | yes | Format version. This document describes `1`. |
| `generator` | string | no | The tool that wrote the file, for diagnostics only. |
| `texture` | string | yes | File name of the PNG, in the same folder as the sheet. |
| `size.w`, `size.h` | integer | yes | Size of the PNG in pixels. |
| `source` | object | no | Where the atlas came from, so it can be regenerated (see below). |
| `settings` | object | no | Packing options used to build the atlas (see below). Absent in sheets converted from another format. |
| `sprites` | array | yes | One entry per sprite. May be empty. |

### Sprites

| Field | Type | Required | Meaning |
|---|---|---|---|
| `name` | string | yes | Unique within the sheet. Engines should use it as the sprite's identity. |
| `x`, `y` | integer | yes | Top-left corner of the sprite in the PNG. |
| `w`, `h` | integer | yes | Size in pixels, both greater than zero. |
| `pivot.x`, `pivot.y` | number | no | Normalized pivot inside the sprite. Default `0.5, 0.5` (the center). |
| `border.left`, `.top`, `.right`, `.bottom` | integer | no | 9-slice borders in pixels. Default `0`. |

**Coordinates start at the top-left corner of the image, with y growing down**,
the way image editors and most 2D engines count. The pivot uses the same
orientation: `(0, 0)` is the sprite's top-left corner, `(1, 1)` its bottom-right.

Engines that put the origin at the bottom-left (Unity, OpenGL texture space) flip
both values:

```
engineY      = size.h - y - h
enginePivotY = 1 - pivot.y
```

Sprites are written in ordinal (byte-wise) order of `name`, so regenerating an
atlas with the same content gives the same file. Readers must not depend on that
order.

A name can be any non-empty string. Atlasmith builds them like this:

- From a Krita file, the name is the layer name, with every character outside
  `a-z A-Z 0-9 _ . -` replaced by `_` (the same rule as Krita's Batch Exporter).
- From a folder of images, the name is the path relative to that folder: folder
  separators become `-`, spaces become `_` and the extension is dropped
  (`Buttons/Play Hover.png` → `Buttons-Play_Hover`). Other characters are kept.

### `source`

| Field | Type | Meaning |
|---|---|---|
| `type` | string | `"krita"` (a `.kra` or `.krz` file) or `"folder"` (a folder of images). |
| `path` | string | Relative to the sheet's folder, with `/` separators. Absolute when there is no relative path, for example on another drive. |
| `ignoreHiddenLayers` | boolean | Krita only: layers hidden in Krita were left out. Default `true`. |

### `settings`

| Field | Type | Meaning |
|---|---|---|
| `padding` | integer | Minimum empty pixels between two sprites. |
| `borderPadding` | integer | Empty pixels between the sprites and the edge of the atlas. |
| `extrude` | integer | Pixels of each sprite's edge repeated outward, against bleeding under bilinear filtering or mipmaps. The sprite rectangle does not include them. |
| `maxSize` | integer | Largest width or height allowed. |
| `powerOfTwo` | boolean | Width and height are powers of two. |
| `forceSquare` | boolean | Width equals height. |

## Compatibility rules

- Readers should ignore fields they don't know. New optional fields can appear
  without a version change.
- A change that an older reader cannot safely ignore raises `version`. A reader
  should refuse a file whose `version` is higher than the one it knows, instead
  of guessing.
- The file is UTF-8 without a byte order mark.

## No rotation, no trimming

Sprites are never rotated, and their transparent edges are never trimmed: the
rectangle in the sheet is exactly the sprite as drawn. A reader doesn't need to
handle offsets or rotated frames.

---

## Em português

O Atlasmith grava cada atlas como um PNG RGBA de 32 bits e uma planilha
`<nome>.atlasmith.json` ao lado. Os campos são os da tabela acima. Os pontos que
mais importam para quem for escrever um leitor:

- **Origem no canto superior esquerdo, y para baixo**, tanto no retângulo quanto no
  pivô. Engines com origem embaixo (Unity) invertem: `yEngine = size.h - y - h` e
  `pivoEngineY = 1 - pivot.y`.
- `pivot` (padrão `0.5, 0.5`) e `border` (padrão `0`) só aparecem quando fogem do padrão.
- O `name` é único na planilha e serve como identidade do sprite.
- Os sprites saem em ordem alfabética (ordinal), mas o leitor não deve depender disso.
- Campos desconhecidos devem ser ignorados. Uma `version` maior do que a conhecida
  deve ser recusada.
- Sem rotação e sem recorte de transparência: o retângulo é o sprite inteiro.
