#!/usr/bin/env python3
# Marabook — regénère le bloc de données de src/View/Icons.cs depuis
# assets/icons/*.svg (13/09/2026). Remplace le make-icons-cs.ps1 resté
# incomplet : ne touche QUE le dictionnaire _paths (entrées triées), le
# bloc runtime (Get/Make/Label) reste tel quel.
#
# Usage : python tools/make-icons-cs.py     (depuis la racine du projet)
# À lancer avec le Python embarqué : python\python.exe tools\make-icons-cs.py

import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ICONS = os.path.join(ROOT, "assets", "icons")
TARGET = os.path.join(ROOT, "src", "View", "Icons.cs")
HEAD = ("        private static readonly Dictionary<string, string> _paths = "
        "new Dictionary<string, string>\n        {\n")


def main():
    raw = io.open(TARGET, "rb").read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    source = raw.decode("utf-8-sig")
    crlf = "\r\n" in source
    source = source.replace("\r\n", "\n")
    if source.count(HEAD) != 1:
        print("Icons.cs : bloc _paths introuvable")
        return 1
    start = source.index(HEAD) + len(HEAD)
    end = source.index("        };", start)
    entries = {}
    for name in sorted(os.listdir(ICONS)):
        if not name.lower().endswith(".svg"):
            continue
        svg = io.open(os.path.join(ICONS, name), encoding="utf-8").read()
        data = " ".join(re.findall(r' d="([^"]+)"', svg))
        if not data:
            print("  (sans <path> : %s ignoré)" % name)
            continue
        entries[name[:-4]] = data.replace('"', "'")
    lines = ['            { "%s", "%s" },' % (k, entries[k]) for k in sorted(entries)]
    source = source[:start] + "\n".join(lines) + "\n" + source[end:]
    if crlf:
        source = source.replace("\n", "\r\n")
    out = source.encode("utf-8")
    if bom:
        out = b"\xef\xbb\xbf" + out
    io.open(TARGET, "wb").write(out)
    print("Icons.cs : %d icônes" % len(entries))
    return 0


if __name__ == "__main__":
    sys.exit(main())
