r"""Exporta cada camada como o Batch Exporter: node.bounds() + projectionPixelData.

É a referência do tools/KraCheck (--oracle): o próprio Krita dizendo como cada
camada deveria sair.

Roda dentro do Krita, sem abrir a interface (Krita 5.x), no PowerShell:

    $env:PYTHONPATH = "tools\krita"
    & "C:\Program Files\Krita (x64)\bin\kritarunner.com" -s kra_oracle <arquivo.kra> <pasta-de-saida>
"""
import json
import os

from krita import Krita

try:
    from PyQt6.QtGui import QImage
except ImportError:
    from PyQt5.QtGui import QImage


def walk(node, depth=0):
    children = node.childNodes()
    for order, child in enumerate(children):
        yield child, depth, order
        yield from walk(child, depth + 1)


def __main__(args):
    src, outdir = args[0], args[1]
    os.makedirs(outdir, exist_ok=True)

    k = Krita.instance()
    k.setBatchmode(True)
    doc = k.openDocument(src)
    doc.waitForDone()
    doc.refreshProjection()
    doc.waitForDone()

    entries = []
    for idx, (node, depth, order) in enumerate(walk(doc.rootNode())):
        b = node.bounds()
        e = {
            "index": idx, "name": node.name(), "type": node.type(), "depth": depth,
            "order": order, "visible": node.visible(), "opacity": node.opacity(),
            "blend": node.blendingMode(), "inheritAlpha": node.inheritAlpha(),
            "x": b.x(), "y": b.y(), "w": b.width(), "h": b.height(),
            "model": node.colorModel(), "depthBits": node.colorDepth(),
            "profile": node.colorProfile(),
        }
        if b.width() > 0 and b.height() > 0 and node.type() in ("paintlayer", "grouplayer"):
            data = node.projectionPixelData(b.x(), b.y(), b.width(), b.height())
            img = QImage(data, b.width(), b.height(), b.width() * 4, QImage.Format.Format_ARGB32).copy()
            fn = "%03d.png" % idx
            img.save(os.path.join(outdir, fn))
            e["file"] = fn
        entries.append(e)

    with open(os.path.join(outdir, "oracle.json"), "w", encoding="utf-8") as f:
        json.dump({"width": doc.width(), "height": doc.height(), "nodes": entries},
                  f, indent=1, ensure_ascii=False)
    doc.close()
    print("ORACLE OK", len(entries), "nodes", flush=True)
