# -*- coding: utf-8 -*-
"""Extrait les textes de l'interface de Marabook (22/09) vers TEXTES-UI.md.

Tous les littéraux de chaîne « lisibles » des sources (src/, hors Theme.cs
qui porte le XAML et hors ressources), regroupés par fichier avec leur
ligne, pour une repasse d'écriture. Les morceaux concaténés par « + » sur
une même instruction sont recollés. Lancer depuis la racine du dépôt :

    .\\python\\python.exe tools\\extraire-textes.py

Le fichier produit est ignoré de git (notes de travail).
"""
import io, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'src')
OUT = os.path.join(ROOT, 'TEXTES-UI.md')
SKIP_FILES = {'Theme.cs', 'Icons.cs', 'IconPicker.cs'}
SKIP_DIRS = {'Print', 'Persistence'}

# Un littéral C# : verbatim @"..." ("" = guillemet) ou classique "..." (\" échappé).
LITERAL = re.compile(r'@"(?:[^"]|"")*"|"(?:[^"\\\n]|\\.)*"')
LINE_COMMENT = re.compile(r'^\s*//')

def unescape(token):
    if token.startswith('@'):
        return token[2:-1].replace('""', '"')
    body = token[1:-1]
    return (body.replace('\\n', '\n').replace('\\"', '"').replace('\\t', '\t')
                .replace('\\\\', '\\').replace('\\u00A0', ' ').replace('\\u202F', ' '))

def readable(text):
    stripped = text.strip()
    if len(stripped) < 12 or ' ' not in stripped:
        return False
    if not re.search(r'[A-Za-zÀ-ÿ]{3}', stripped):
        return False
    letters = sum(1 for c in stripped if c.isalpha())
    if letters < len(stripped) * 0.5:
        return False
    # Du code, pas du texte : XML, chemins, formats, sélecteurs.
    if re.search(r'<[a-zA-Z/]|\{\d|\.cs\b|\\\\|^[a-z0-9_.-]+$|[=;]{2}', stripped):
        return False
    if stripped.startswith(('http', 'file:', 'urn:', 'application/', 'text/')):
        return False
    return True

def strings_of(path):
    """(ligne, texte) pour chaque texte lisible, morceaux recollés."""
    with io.open(path, encoding='utf-8-sig') as f:
        source = f.read()
    # Enlève les commentaires de ligne (les /// aussi) pour ne garder que le code.
    lines = source.split('\n')
    kept = []
    for line in lines:
        kept.append('' if LINE_COMMENT.match(line) else line)
    code = '\n'.join(kept)
    results = []
    pos = 0
    current = None  # [ligne, texte, fin]
    for m in LITERAL.finditer(code):
        between = code[pos:m.start()] if current else None
        line = code.count('\n', 0, m.start()) + 1
        text = unescape(m.group(0))
        if current is not None and between is not None and re.fullmatch(r'\s*\+\s*', between):
            current[1] += text
        else:
            if current is not None:
                results.append((current[0], current[1]))
            current = [line, text]
        pos = m.end()
    if current is not None:
        results.append((current[0], current[1]))
    seen = set()
    out = []
    for line, text in results:
        if not readable(text):
            continue
        key = text.strip()
        if key in seen:
            continue
        seen.add(key)
        out.append((line, key))
    return out

def main():
    files = []
    for folder, dirs, names in os.walk(SRC):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in sorted(names):
            if name.endswith('.cs') and name not in SKIP_FILES:
                files.append(os.path.join(folder, name))
    files.sort(key=lambda p: os.path.relpath(p, SRC).lower())
    total = 0
    with io.open(OUT, 'w', encoding='utf-8', newline='\n') as out:
        out.write('# Textes de l\'interface de Marabook\n\n')
        out.write('Produit par `tools/extraire-textes.py` : chaque texte lisible des sources, par fichier et ligne, '
                  'pour une repasse d\'écriture. Modifiez le texte dans la source (la ligne indiquée), '
                  'pas ici — ce fichier est régénéré et ignoré de git.\n\n')
        for path in files:
            entries = strings_of(path)
            if not entries:
                continue
            rel = os.path.relpath(path, ROOT).replace('\\', '/')
            out.write('## ' + rel + '\n\n')
            for line, text in entries:
                shown = text.replace('\n', ' ⏎ ')
                out.write('- L' + str(line) + ' : ' + shown + '\n')
                total += 1
            out.write('\n')
    print('TEXTES-UI.md : ' + str(total) + ' textes dans ' + str(len(files)) + ' fichiers')

if __name__ == '__main__':
    main()
