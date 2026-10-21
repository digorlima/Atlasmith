r"""Cria um .kra sintético cobrindo os casos que o leitor do Atlasmith precisa tratar.

Roda dentro do Krita, sem abrir a interface (Krita 5.x). No Git Bash, de dentro
de tools/krita (no PowerShell o PYTHONPATH não chega ao kritarunner):

    PYTHONPATH="$PWD" "/c/Program Files/Krita (x64)/bin/kritarunner.com" -s make_test_kra <saida/kra-test.kra>

Com a extensão .krz o Krita grava a versão de arquivamento.
"""
import math
import os
import random

from krita import Krita, Selection


def fill(node, x, y, w, h, fn):
    """fn(i, j) -> (r, g, b, a). Grava BGRA como o espaço RGBA/U8 do Krita espera."""
    buf = bytearray(w * h * 4)
    p = 0
    for j in range(h):
        for i in range(w):
            r, g, b, a = fn(i, j)
            buf[p] = b; buf[p + 1] = g; buf[p + 2] = r; buf[p + 3] = a
            p += 4
    node.setPixelData(bytes(buf), x, y, w, h)


def blob(seed, cx, cy, rad, rgb):
    """Disco com borda suave e ruído na cor — exercita alfa parcial e LZF."""
    rnd = random.Random(seed)
    def fn(i, j):
        d = math.hypot(i - cx, j - cy)
        a = max(0.0, min(1.0, rad - d))
        if a <= 0:
            return (0, 0, 0, 0)
        n = rnd.randint(-25, 25)
        return (max(0, min(255, rgb[0] + n)), max(0, min(255, rgb[1] + n)),
                max(0, min(255, rgb[2] + n)), int(round(a * 255)))
    return fn


def __main__(args):
    out = args[0]
    k = Krita.instance()
    k.setBatchmode(True)

    doc = k.createDocument(512, 384, "kra-test", "RGBA", "U8", "", 300.0)
    root = doc.rootNode()
    for c in root.childNodes():
        c.remove()

    def add(parent, node):
        parent.addChildNode(node, None)  # None = no topo da pilha
        return node

    # 0. Fundo branco, como o "Plano de Fundo" de um documento novo.
    bg = add(root, doc.createNode("Plano de Fundo", "paintlayer"))
    fill(bg, 0, 0, 512, 384, lambda i, j: (255, 255, 255, 255))

    # 1. Camada simples, deslocada e saindo do canvas pela esquerda/topo.
    plain = add(root, doc.createNode("test-plain", "paintlayer"))
    fill(plain, 0, 0, 150, 120, blob(1, 60, 50, 55, (220, 80, 40)))
    plain.move(-40, -20)

    # 2. Opacidade 50% e modo multiply na raiz (o projection deve ignorar ambos).
    op = add(root, doc.createNode("test-opacity", "paintlayer"))
    fill(op, 300, 10, 90, 90, blob(2, 45, 45, 40, (40, 200, 90)))
    op.setOpacity(128)
    op.setBlendingMode("multiply")

    # 3. Desativada: fica de fora por padrão e entra com "Ignorar camadas desativadas" desligado.
    hid = add(root, doc.createNode("test-hidden", "paintlayer"))
    fill(hid, 400, 200, 60, 60, blob(3, 30, 30, 28, (90, 90, 250)))
    hid.setVisible(False)

    # 4. Máscara de transparência escondendo a metade direita.
    masked = add(root, doc.createNode("test-mask", "paintlayer"))
    fill(masked, 20, 200, 120, 120, blob(4, 60, 60, 58, (250, 220, 60)))
    tm = doc.createTransparencyMask("mascara")
    sel = Selection()
    sel.select(0, 0, 80, 512, 255)  # só x < 80 fica visível
    tm.setSelection(sel)
    masked.addChildNode(tm, None)

    # 5. Grupo nomeado = um sprite composto.
    grp = add(root, doc.createGroupLayer("test-group"))
    base = add(grp, doc.createNode("base", "paintlayer"))
    fill(base, 200, 150, 100, 100, blob(5, 50, 50, 48, (60, 140, 230)))
    mul = add(grp, doc.createNode("sombra", "paintlayer"))
    fill(mul, 230, 180, 90, 60, blob(6, 45, 30, 40, (120, 60, 160)))
    mul.setBlendingMode("multiply")
    mul.setOpacity(200)
    scr = add(grp, doc.createNode("brilho", "paintlayer"))
    fill(scr, 210, 160, 50, 50, blob(7, 25, 25, 22, (255, 240, 200)))
    scr.setBlendingMode("screen")
    clip = add(grp, doc.createNode("recorte", "paintlayer"))
    fill(clip, 180, 140, 140, 40, lambda i, j: (255, 255, 255, 180))
    clip.setInheritAlpha(True)
    ghost = add(grp, doc.createNode("fantasma", "paintlayer"))
    fill(ghost, 330, 300, 40, 40, lambda i, j: (255, 0, 0, 255))
    ghost.setVisible(False)
    half = add(grp, doc.createNode("meia-opacidade", "paintlayer"))
    fill(half, 260, 220, 40, 40, lambda i, j: (20, 220, 20, 255))
    half.setOpacity(100)

    # 6. Grupo organizacional com filhos no padrão.
    org = add(root, doc.createGroupLayer("Pasta de botões"))
    a = add(org, doc.createNode("test-child-a", "paintlayer"))
    fill(a, 10, 120, 40, 30, blob(8, 20, 15, 14, (200, 200, 200)))
    b = add(org, doc.createNode("test-child-b", "paintlayer"))
    fill(b, 60, 120, 40, 30, blob(9, 20, 15, 14, (30, 30, 30)))

    # 6b. Grupo organizacional desativado: os filhos herdam o "desativado".
    off = add(root, doc.createGroupLayer("Rascunhos"))
    draft = add(off, doc.createNode("test-draft", "paintlayer"))
    fill(draft, 150, 330, 50, 40, blob(10, 25, 20, 18, (240, 120, 200)))
    off.setVisible(False)

    # 7. Nomes fora do padrão (o fundo já foi criado primeiro, então fica embaixo).
    junk = add(root, doc.createNode("Camada 1", "paintlayer"))
    fill(junk, 5, 5, 10, 10, lambda i, j: (0, 0, 0, 255))
    single = add(root, doc.createNode("logo", "paintlayer"))
    fill(single, 450, 10, 20, 20, lambda i, j: (0, 0, 255, 255))

    doc.refreshProjection()
    doc.waitForDone()
    os.makedirs(os.path.dirname(out), exist_ok=True)
    ok = doc.saveAs(out)
    doc.waitForDone()
    print("SAVED", ok, out, [(c.name(), c.type()) for c in root.childNodes()], flush=True)
    doc.close()
