#!/usr/bin/env python3
# Marabook — vectorisation d'une icône PNG en SVG (13/09/2026).
#
# Aucun outil de tracé n'est installé sur le poste (ni potrace, ni
# Inkscape) : ce script trace lui-même un masque de pixels en contours
# polygonaux simplifiés, dans une viewBox 256×256 comme les icônes
# Phosphor du jeu embarqué. Il attend en entrée un MASQUE texte (une
# ligne par rangée, « 1 » = pixel encré, « 0 » = vide), produit depuis le
# PNG par PowerShell/System.Drawing (le Python embarqué n'a pas Pillow) :
#
#   $b = [System.Drawing.Bitmap]::FromFile("icone.png")
#   ... pour chaque pixel : '1' si alpha > 127 et sombre, '0' sinon ...
#
# Usage : python tools/trace-png-icon.py masque.txt sortie.svg [epsilon]
#
# Méthode : sous-échantillonnage du masque à 256 (majorité), arêtes de
# bord des cellules encrées orientées « encre à gauche », chaînage en
# boucles fermées, simplification Douglas-Peucker (epsilon en unités de
# la viewBox, 1,2 par défaut), un seul <path> avec fill-rule evenodd (les
# trous sont des boucles à part — l'orientation n'importe pas).

import sys
import io
import math


def load_mask(path):
    rows = [line.rstrip("\n") for line in io.open(path, encoding="utf-8")]
    rows = [r for r in rows if r]
    return rows


def downsample(rows, size=256):
    h = len(rows)
    w = len(rows[0])
    fy = h / size
    fx = w / size
    grid = [[0] * size for _ in range(size)]
    for y in range(size):
        y0, y1 = int(y * fy), max(int((y + 1) * fy), int(y * fy) + 1)
        for x in range(size):
            x0, x1 = int(x * fx), max(int((x + 1) * fx), int(x * fx) + 1)
            filled = 0
            total = 0
            for yy in range(y0, y1):
                row = rows[yy]
                for xx in range(x0, x1):
                    total += 1
                    if row[xx] == "1":
                        filled += 1
            grid[y][x] = 1 if filled * 2 >= total else 0
    return grid


def boundary_edges(grid):
    size = len(grid)
    edges = {}  # start -> list of ends (encre à gauche du sens de parcours)

    def add(a, b):
        edges.setdefault(a, []).append(b)

    for y in range(size):
        for x in range(size):
            if not grid[y][x]:
                continue
            if y == 0 or not grid[y - 1][x]:
                add((x, y), (x + 1, y))            # haut : gauche → droite
            if x == size - 1 or not grid[y][x + 1]:
                add((x + 1, y), (x + 1, y + 1))    # droite : haut → bas
            if y == size - 1 or not grid[y + 1][x]:
                add((x + 1, y + 1), (x, y + 1))    # bas : droite → gauche
            if x == 0 or not grid[y][x - 1]:
                add((x, y + 1), (x, y))            # gauche : bas → haut
    return edges


def chain_loops(edges):
    loops = []
    while edges:
        start = next(iter(edges))
        loop = [start]
        current = start
        previous = None
        while True:
            outs = edges.get(current)
            if not outs:
                break
            # Au croisement (deux sorties), tourner le plus à droite par
            # rapport à l'arrivée, pour ne pas mélanger deux contours.
            if len(outs) > 1 and previous is not None:
                dx, dy = current[0] - previous[0], current[1] - previous[1]
                best = None
                best_score = None
                for candidate in outs:
                    ex, ey = candidate[0] - current[0], candidate[1] - current[1]
                    cross = dx * ey - dy * ex
                    dot = dx * ex + dy * ey
                    score = math.atan2(cross, dot)
                    if best is None or score < best_score:
                        best, best_score = candidate, score
                nxt = best
            else:
                nxt = outs[0]
            outs.remove(nxt)
            if not outs:
                del edges[current]
            previous = current
            current = nxt
            if current == start:
                break
            loop.append(current)
        if len(loop) >= 3:
            loops.append(loop)
    return loops


def simplify(points, epsilon):
    "Douglas-Peucker sur une boucle fermée (le premier point est fixé)."
    if len(points) < 4:
        return points

    def dp(pts):
        if len(pts) < 3:
            return pts
        (x0, y0), (x1, y1) = pts[0], pts[-1]
        dx, dy = x1 - x0, y1 - y0
        norm = math.hypot(dx, dy) or 1.0
        index, dmax = 0, 0.0
        for i in range(1, len(pts) - 1):
            px, py = pts[i]
            d = abs(dy * px - dx * py + x1 * y0 - y1 * x0) / norm
            if d > dmax:
                index, dmax = i, d
        if dmax > epsilon:
            left = dp(pts[:index + 1])
            right = dp(pts[index:])
            return left[:-1] + right
        return [pts[0], pts[-1]]

    # Couper la boucle en deux au point le plus éloigné du premier.
    far = max(range(len(points)), key=lambda i: (points[i][0] - points[0][0]) ** 2
              + (points[i][1] - points[0][1]) ** 2)
    first = dp(points[:far + 1])
    second = dp(points[far:] + [points[0]])
    result = first[:-1] + second[:-1]
    return result


def fmt(v):
    return ("%.1f" % v).rstrip("0").rstrip(".")


def to_path(loops, epsilon):
    parts = []
    for loop in loops:
        pts = simplify(loop, epsilon)
        if len(pts) < 3:
            continue
        parts.append("M" + " ".join(fmt(x) + "," + fmt(y) for x, y in pts) + "Z")
    return "".join(parts)


def main():
    if len(sys.argv) < 3:
        print("usage : trace-png-icon.py masque.txt sortie.svg [epsilon]")
        return 2
    epsilon = float(sys.argv[3]) if len(sys.argv) > 3 else 1.2
    grid = downsample(load_mask(sys.argv[1]))
    loops = chain_loops(boundary_edges(grid))
    d = to_path(loops, epsilon)
    svg = ('<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" fill="#000000" '
           'viewBox="0 0 256 256" fill-rule="evenodd"><path d="%s"></path></svg>\n' % d)
    io.open(sys.argv[2], "w", encoding="utf-8", newline="\n").write(svg)
    print("%s : %d boucles, %d octets" % (sys.argv[2], len(loops), len(svg)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
