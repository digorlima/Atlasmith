"""Gera uma pasta de sprites de teste para o Atlasmith.

Usa os tamanhos e nomes reais do atlas Ingame_Panel (o exemplo de .tpsheet que
originou o formato), incluindo espacos nos nomes e subpastas, para exercitar a
regra de nomeacao alem do empacotamento.

Uso: python tools/make_testset.py [destino]
"""

from __future__ import annotations

import os
import struct
import sys
import zlib

# (subpasta, arquivo, largura, altura) - extraidos do Ingame_Panel.tpsheet.
SPRITES = [
    ("Action", "Option Ornament m012", 195, 196),
    ("Action", "Ornament m012", 886, 228),
    ("Action", "Pointer m012", 180, 463),
    ("Action Order", "Crystal m012", 86, 160),
    ("Action Order", "Frame m012", 314, 556),
    ("Action Order", "Mask m012", 240, 522),
    ("Action Order", "Ornament BG m012", 246, 497),
    ("Action Order", "Ornament Bottom m012", 360, 323),
    ("Ally Bar", "Ornament Borders m012", 182, 130),
    ("Ally Bar", "Ornament Center Borders m012", 179, 137),
    ("Ally Bar", "Ornament Center Bottom m012", 188, 88),
    ("Ally Bar", "Ornament Center Top m012", 166, 129),
    ("Ally Bar", "Overdrive Mask m012", 466, 292),
    ("Change", "Ornament Back m012", 136, 107),
    ("Change", "Ornament Front m012", 170, 116),
    ("Clock", "Crystal m012", 70, 130),
    ("Clock", "Ornament Center m012", 440, 420),
    ("Clock", "Ornament Top Back m012", 427, 287),
    ("Clock", "Ornament Top Front m012", 375, 201),
    ("Clock", "Pointer m012", 106, 262),
    ("Global Mana", "Frame FIrst m012", 78, 255),
    ("Global Mana", "Frame Second m012", 78, 255),
    ("Global Mana", "Frame Third m012", 107, 318),
    ("Global Mana", "Knob m012", 81, 136),
    ("Global Mana", "Mask First m012", 55, 203),
    ("Global Mana", "Mask Second m012", 55, 203),
    ("Global Mana", "Mask Third m012", 71, 265),
    ("Skill Label", "Ornament m012", 641, 100),
]

PALETTE = [
    (0x7A, 0x5C, 0xFF), (0x4C, 0x8D, 0xFF), (0x3E, 0xCF, 0x8E), (0xF5, 0xA5, 0x24),
    (0xFF, 0x5A, 0x66), (0xE8, 0x6B, 0xD2), (0x36, 0xC5, 0xD0), (0xB6, 0xD9, 0x3B),
]


def chunk(tag: bytes, data: bytes) -> bytes:
    return (struct.pack(">I", len(data)) + tag + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))


def make_png(width: int, height: int, color: tuple[int, int, int]) -> bytes:
    """Retangulo solido com borda mais clara e um canto transparente."""
    r, g, b = color
    light = (min(255, r + 70), min(255, g + 70), min(255, b + 70))

    raw = bytearray()
    for y in range(height):
        raw.append(0)  # filtro None
        for x in range(width):
            edge = x < 3 or y < 3 or x >= width - 3 or y >= height - 3
            # Canto superior esquerdo transparente: valida o canal alfa no atlas.
            if x < 6 and y < 6:
                raw += bytes((0, 0, 0, 0))
            elif edge:
                raw += bytes((light[0], light[1], light[2], 255))
            else:
                raw += bytes((r, g, b, 255))

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", ihdr)
            + chunk(b"IDAT", zlib.compress(bytes(raw), 6))
            + chunk(b"IEND", b""))


def main() -> None:
    target = sys.argv[1] if len(sys.argv) > 1 else "testset"
    total_area = 0

    for i, (folder, name, w, h) in enumerate(SPRITES):
        directory = os.path.join(target, folder)
        os.makedirs(directory, exist_ok=True)
        path = os.path.join(directory, name + ".png")
        with open(path, "wb") as fh:
            fh.write(make_png(w, h, PALETTE[i % len(PALETTE)]))
        total_area += w * h

    print(f"{len(SPRITES)} sprites gravados em {os.path.abspath(target)}")
    print(f"area somada dos sprites: {total_area} px "
          f"({total_area / (1024 * 2048) * 100:.1f}% de um atlas 1024x2048)")


if __name__ == "__main__":
    main()
