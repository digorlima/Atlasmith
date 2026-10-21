"""Gera Assets/icon.ico do Atlasmith.

Sem dependencias externas: escreve os PNGs na mao (zlib + struct) e monta o
container ICO. Rode com `python tools/make_icon.py` a partir da raiz do projeto.
"""

from __future__ import annotations

import os
import struct
import zlib

SIZES = [16, 24, 32, 48, 64, 128, 256]
SS = 4  # supersampling para suavizar as bordas

# Gradiente do fundo (canto superior esquerdo -> canto inferior direito).
GRAD_A = (0x7A, 0x5C, 0xFF)
GRAD_B = (0x4C, 0x8D, 0xFF)

# Retangulos brancos internos: (x0, y0, x1, y1, alpha), em fracao do lado.
BLOCKS = [
    (0.215, 0.215, 0.500, 0.620, 1.00),
    (0.545, 0.215, 0.785, 0.385, 0.82),
    (0.545, 0.420, 0.785, 0.620, 0.66),
    (0.215, 0.655, 0.785, 0.785, 0.90),
]


def rounded_coverage(px: float, py: float, x0: float, y0: float,
                     x1: float, y1: float, radius: float) -> bool:
    """True se o ponto esta dentro do retangulo de cantos arredondados."""
    if px < x0 or px > x1 or py < y0 or py > y1:
        return False

    cx = min(max(px, x0 + radius), x1 - radius)
    cy = min(max(py, y0 + radius), y1 - radius)
    dx = px - cx
    dy = py - cy
    return dx * dx + dy * dy <= radius * radius


def render(size: int) -> bytes:
    """Desenha o icone em RGBA, retornando os bytes crus."""
    hi = size * SS
    out = bytearray(size * size * 4)

    outer_r = hi * 0.235
    block_r = hi * 0.035

    # Renderiza em alta resolucao e depois faz a media dos blocos SSxSS.
    hi_pixels = bytearray(hi * hi * 4)
    for y in range(hi):
        for x in range(hi):
            idx = (y * hi + x) * 4
            if not rounded_coverage(x + 0.5, y + 0.5, 0, 0, hi, hi, outer_r):
                continue

            t = (x + y) / (2.0 * hi)
            r = int(GRAD_A[0] + (GRAD_B[0] - GRAD_A[0]) * t)
            g = int(GRAD_A[1] + (GRAD_B[1] - GRAD_A[1]) * t)
            b = int(GRAD_A[2] + (GRAD_B[2] - GRAD_A[2]) * t)
            a = 255

            for bx0, by0, bx1, by1, alpha in BLOCKS:
                if rounded_coverage(x + 0.5, y + 0.5,
                                    bx0 * hi, by0 * hi, bx1 * hi, by1 * hi,
                                    block_r):
                    r = int(r + (255 - r) * alpha)
                    g = int(g + (255 - g) * alpha)
                    b = int(b + (255 - b) * alpha)
                    break

            hi_pixels[idx] = r
            hi_pixels[idx + 1] = g
            hi_pixels[idx + 2] = b
            hi_pixels[idx + 3] = a

    # Downsample com media simples.
    area = SS * SS
    for y in range(size):
        for x in range(size):
            rs = gs = bs = as_ = 0
            for sy in range(SS):
                for sx in range(SS):
                    si = ((y * SS + sy) * hi + (x * SS + sx)) * 4
                    a = hi_pixels[si + 3]
                    rs += hi_pixels[si] * a
                    gs += hi_pixels[si + 1] * a
                    bs += hi_pixels[si + 2] * a
                    as_ += a

            di = (y * size + x) * 4
            if as_ == 0:
                continue
            out[di] = rs // as_
            out[di + 1] = gs // as_
            out[di + 2] = bs // as_
            out[di + 3] = as_ // area

    return bytes(out)


def chunk(tag: bytes, data: bytes) -> bytes:
    return (struct.pack(">I", len(data)) + tag + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))


def to_png(rgba: bytes, size: int) -> bytes:
    raw = bytearray()
    stride = size * 4
    for y in range(size):
        raw.append(0)  # filtro None
        raw += rgba[y * stride:(y + 1) * stride]

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", ihdr)
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))


def main() -> None:
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    target = os.path.join(root, "src", "Atlasmith", "Assets", "icon.ico")
    os.makedirs(os.path.dirname(target), exist_ok=True)

    images = []
    for size in SIZES:
        print(f"  renderizando {size}x{size}...")
        images.append((size, to_png(render(size), size)))

    header = struct.pack("<HHH", 0, 1, len(images))
    offset = len(header) + 16 * len(images)

    entries = bytearray()
    body = bytearray()
    for size, png in images:
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(png), offset)
        body += png
        offset += len(png)

    with open(target, "wb") as fh:
        fh.write(header + bytes(entries) + bytes(body))

    print(f"icone gravado em {target} ({len(header) + len(entries) + len(body)} bytes)")


if __name__ == "__main__":
    main()
