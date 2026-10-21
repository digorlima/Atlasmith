"""Confere um par atlas.png + atlas.tpsheet contra a pasta de sprites original.

Validacao independente do codigo C#: decodifica os PNGs na mao e compara pixel a
pixel o que foi para o atlas com o arquivo de origem, alem de checar o formato do
.tpsheet, as margens e a ausencia de sobreposicao.

Uso:
    python tools/verify_atlas.py <atlas.tpsheet> <pasta_de_sprites> [--padding N] [--border N]
"""

from __future__ import annotations

import os
import struct
import sys
import zlib

failures: list[str] = []


def fail(msg: str) -> None:
    failures.append(msg)
    print(f"  FALHA: {msg}")


def ok(msg: str) -> None:
    print(f"  ok - {msg}")


# ----------------------------------------------------------------------
# Decoder PNG (apenas o necessario: 8 bits, RGBA/RGB/gray, nao entrelacado)
# ----------------------------------------------------------------------

def paeth(a: int, b: int, c: int) -> int:
    p = a + b - c
    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
    if pa <= pb and pa <= pc:
        return a
    return b if pb <= pc else c


def decode_png(path: str) -> tuple[int, int, bytearray]:
    """Retorna (largura, altura, pixels RGBA)."""
    data = open(path, "rb").read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"{path}: nao e um PNG")

    pos = 8
    width = height = depth = color = 0
    idat = bytearray()

    while pos < len(data):
        length = struct.unpack(">I", data[pos:pos + 4])[0]
        tag = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + length]
        pos += 12 + length

        if tag == b"IHDR":
            width, height, depth, color, _, _, interlace = struct.unpack(">IIBBBBB", body)
            if depth != 8:
                raise ValueError(f"{path}: profundidade {depth} nao suportada")
            if interlace:
                raise ValueError(f"{path}: entrelacado nao suportado")
        elif tag == b"IDAT":
            idat += body
        elif tag == b"IEND":
            break

    channels = {0: 1, 2: 3, 4: 2, 6: 4}[color]
    raw = zlib.decompress(bytes(idat))
    stride = width * channels

    out = bytearray(width * height * 4)
    prev = bytearray(stride)
    src = 0

    for y in range(height):
        ftype = raw[src]
        src += 1
        line = bytearray(raw[src:src + stride])
        src += stride

        if ftype == 1:
            for i in range(channels, stride):
                line[i] = (line[i] + line[i - channels]) & 0xFF
        elif ftype == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif ftype == 3:
            for i in range(stride):
                left = line[i - channels] if i >= channels else 0
                line[i] = (line[i] + ((left + prev[i]) >> 1)) & 0xFF
        elif ftype == 4:
            for i in range(stride):
                left = line[i - channels] if i >= channels else 0
                upleft = prev[i - channels] if i >= channels else 0
                line[i] = (line[i] + paeth(left, prev[i], upleft)) & 0xFF
        elif ftype != 0:
            raise ValueError(f"{path}: filtro {ftype} desconhecido")

        base = y * width * 4
        for x in range(width):
            s = x * channels
            if channels == 4:
                out[base + x * 4:base + x * 4 + 4] = line[s:s + 4]
            elif channels == 3:
                out[base + x * 4:base + x * 4 + 3] = line[s:s + 3]
                out[base + x * 4 + 3] = 255
            elif channels == 2:
                g = line[s]
                out[base + x * 4:base + x * 4 + 4] = bytes((g, g, g, line[s + 1]))
            else:
                g = line[s]
                out[base + x * 4:base + x * 4 + 4] = bytes((g, g, g, 255))

        prev = line

    return width, height, out


# ----------------------------------------------------------------------

def sprite_name(root: str, path: str) -> str:
    rel = os.path.relpath(path, root)
    rel = os.path.splitext(rel)[0]
    return rel.replace(os.sep, "-").replace("/", "-").replace(" ", "_")


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 2

    sheet_path = sys.argv[1]
    sprite_root = sys.argv[2]
    padding = 0
    border = 0
    args = sys.argv[3:]
    for i, a in enumerate(args):
        if a == "--padding":
            padding = int(args[i + 1])
        elif a == "--border":
            border = int(args[i + 1])

    lines = open(sheet_path, encoding="utf-8").read().split("\n")
    header = [l for l in lines if l.startswith("#") or l.startswith(":")]
    entries = [l for l in lines if l and not l.startswith("#") and not l.startswith(":")]

    print("Cabecalho")
    meta = {}
    for line in header:
        if line.startswith(":"):
            key, _, value = line[1:].partition("=")
            meta[key] = value

    for key, expected in [("format", "40300"), ("pivotpoints", "enabled"),
                          ("borders", "disabled"),
                          ("alphahandling", "ClearTransparentPixels")]:
        if meta.get(key) == expected:
            ok(f":{key}={expected}")
        else:
            fail(f":{key} esperado {expected}, veio {meta.get(key)}")

    atlas_png = os.path.join(os.path.dirname(sheet_path), meta.get("texture", ""))
    if not os.path.exists(atlas_png):
        fail(f"textura declarada nao existe: {atlas_png}")
        return 1
    ok(f":texture={meta['texture']}")

    print("\nImagem do atlas")
    aw, ah, apx = decode_png(atlas_png)
    if meta.get("size") == f"{aw}x{ah}":
        ok(f":size bate com o PNG ({aw}x{ah})")
    else:
        fail(f":size={meta.get('size')} mas o PNG e {aw}x{ah}")

    # Sprites de origem
    sources = {}
    for folder, _, files in os.walk(sprite_root):
        for fname in files:
            if fname.lower().endswith(".png"):
                full = os.path.join(folder, fname)
                sources[sprite_name(sprite_root, full)] = full

    print(f"\nEntradas ({len(entries)} linhas, {len(sources)} arquivos de origem)")
    if len(entries) == len(sources):
        ok("uma linha por sprite")
    else:
        fail(f"{len(entries)} linhas para {len(sources)} arquivos")

    rects = []
    mismatched = 0
    checked = 0

    for line in entries:
        parts = line.split(";")
        if len(parts) != 11:
            fail(f"linha com {len(parts)} campos: {line[:60]}")
            continue

        name = parts[0]
        x, y_bottom, w, h = (int(parts[1]), int(parts[2]), int(parts[3]), int(parts[4]))
        y_top = ah - y_bottom - h
        rects.append((name, x, y_top, w, h))

        if name not in sources:
            fail(f"nome sem arquivo correspondente: {name}")
            continue

        sw, sh, spx = decode_png(sources[name])
        if (sw, sh) != (w, h):
            fail(f"{name}: sheet diz {w}x{h}, arquivo e {sw}x{sh}")
            continue

        # Compara os pixels realmente gravados no atlas.
        diff = 0
        for row in range(sh):
            src_off = row * sw * 4
            dst_off = ((y_top + row) * aw + x) * 4
            if spx[src_off:src_off + sw * 4] != apx[dst_off:dst_off + sw * 4]:
                diff += 1
        checked += 1
        if diff:
            mismatched += 1
            fail(f"{name}: {diff} linha(s) de pixel diferentes do arquivo original")

    if mismatched == 0 and checked:
        ok(f"pixels identicos ao original nos {checked} sprites")

    print("\nGeometria")
    inside = True
    for name, x, y, w, h in rects:
        if x < border or y < border or x + w > aw - border or y + h > ah - border:
            fail(f"{name} fora dos limites uteis ({x},{y} {w}x{h})")
            inside = False
    if inside:
        ok(f"todos dentro do atlas, respeitando a margem de borda de {border}px")

    overlaps = 0
    close = 0
    for i in range(len(rects)):
        _, ax, ay, aw_, ah_ = rects[i]
        for j in range(i + 1, len(rects)):
            _, bx, by, bw, bh = rects[j]
            if ax < bx + bw and ax + aw_ > bx and ay < by + bh and ay + ah_ > by:
                overlaps += 1
                continue
            gap_x = max(bx - (ax + aw_), ax - (bx + bw))
            gap_y = max(by - (ay + ah_), ay - (by + bh))
            if max(gap_x, gap_y) < padding:
                close += 1

    if overlaps:
        fail(f"{overlaps} par(es) sobrepostos")
    else:
        ok("nenhuma sobreposicao")

    if close:
        fail(f"{close} par(es) mais proximos que {padding}px")
    else:
        ok(f"margem de {padding}px respeitada")

    print()
    if failures:
        print(f"{len(failures)} FALHA(S).")
        return 1
    print("TUDO CERTO.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
